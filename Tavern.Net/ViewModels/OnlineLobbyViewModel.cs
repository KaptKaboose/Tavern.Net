using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Decklists;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.Online;

namespace Tavern.Net.ViewModels;

/// <summary>
/// The "Online" screen: host or join a direct connection, ready up, roll for turn order, and (host
/// only) start. Each player's deck is already chosen before reaching here (the active deck from
/// Change Deck, same as Solo) — this screen is purely about the connection and the handshake.
/// </summary>
public sealed partial class OnlineLobbyViewModel : ObservableObject
{
    // An arbitrary fixed port — reachability (same LAN, Tailscale, a forwarded port) is the
    // players' concern, not this screen's; see the app's own "Online" design notes.
    private const int DefaultPort = 51900;

    private readonly GrandArchiveApiClient _apiClient;
    private readonly DeckStorageService _deckStorage;
    private readonly GameStorageService _gameStorage;
    private readonly PlayerNameStorageService _playerNameStorage;

    private GameConnection? _connection;

    [ObservableProperty]
    private string _playerName;

    [ObservableProperty]
    private string _joinAddress = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartGameCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuest), nameof(IsMeChosenFirst), nameof(IsOpponentChosenFirst))]
    private bool _isHost;

    public bool IsGuest => !IsHost;

    [ObservableProperty]
    private string? _hostAddress;

    [ObservableProperty]
    private string? _opponentName;

    /// <summary>The opponent's app version, from their Hello — null until then, and also null from a
    /// build old enough not to send one (see <see cref="VersionMismatchMessage"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionMismatchMessage))]
    [NotifyCanExecuteChangedFor(nameof(StartGameCommand))]
    private string? _opponentVersion;

    private bool _opponentHasGreeted;

    public string OwnVersionText => $"Version {AppVersion.Display}";

    /// <summary>Shown when the two players are on different versions — starting is blocked until
    /// they match, since builds that differ can disagree on the wire format or the rules.</summary>
    public string? VersionMismatchMessage => AppVersion.DescribeMismatch(AppVersion.Display, OpponentVersion, _opponentHasGreeted);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartGameCommand))]
    private bool _isReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartGameCommand))]
    private bool _opponentReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFirstPlayer), nameof(BothRolled), nameof(IsDiceTied), nameof(IsWaitingForOpponentChoice), nameof(ShowChoiceButtons))]
    [NotifyCanExecuteChangedFor(nameof(RollDiceCommand), nameof(ChooseMeFirstCommand), nameof(ChooseOpponentFirstCommand))]
    private int? _myDiceTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFirstPlayer), nameof(BothRolled), nameof(IsDiceTied), nameof(IsWaitingForOpponentChoice), nameof(ShowChoiceButtons))]
    [NotifyCanExecuteChangedFor(nameof(RollDiceCommand), nameof(ChooseMeFirstCommand), nameof(ChooseOpponentFirstCommand))]
    private int? _opponentDiceTotal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartGameCommand))]
    [NotifyPropertyChangedFor(nameof(IsMeChosenFirst), nameof(IsOpponentChosenFirst), nameof(ShowChoiceButtons), nameof(IsWaitingForOpponentChoice), nameof(HasChosenFirstPlayer))]
    private int? _firstPlayerNumber;

    public ObservableCollection<DieViewModel> MyDice { get; } = new();

    public ObservableCollection<DieViewModel> OpponentDice { get; } = new();

    public bool BothRolled => MyDiceTotal.HasValue && OpponentDiceTotal.HasValue;

    public bool IsDiceTied => BothRolled && MyDiceTotal == OpponentDiceTotal;

    /// <summary>Only whoever rolled strictly higher gets to pick who goes first — a tie (or nobody
    /// having rolled yet) means neither side can choose until someone rolls higher (a tie re-enables
    /// Roll for both — see CanRollDice). Still purely informational/unenforced everywhere else, same
    /// as the roll itself.</summary>
    public bool CanChooseFirstPlayer => BothRolled && MyDiceTotal > OpponentDiceTotal;

    /// <summary>The choice buttons themselves — visible (not just enabled) only for the side that
    /// rolled higher, and only until a choice has actually been made, at which point they're
    /// replaced by a persistent confirmation both sides see (see HasChosenFirstPlayer).</summary>
    public bool ShowChoiceButtons => CanChooseFirstPlayer && !FirstPlayerNumber.HasValue;

    /// <summary>True for the side that rolled lower once both have rolled — shown a "waiting on
    /// them" message instead of the (hidden, not just disabled) choice buttons.</summary>
    public bool IsWaitingForOpponentChoice => BothRolled && !IsDiceTied && !CanChooseFirstPlayer && !FirstPlayerNumber.HasValue;

    public bool HasChosenFirstPlayer => FirstPlayerNumber.HasValue;

    private int MyPlayerNumber => IsHost ? 0 : 1;

    public bool IsMeChosenFirst => FirstPlayerNumber == MyPlayerNumber;

    public bool IsOpponentChosenFirst => FirstPlayerNumber.HasValue && FirstPlayerNumber != MyPlayerNumber;

    /// <summary>Raised once this side's local GameSession is built and ready — MainViewModel hands
    /// off to the game board, passing the same GameConnection through so it can keep broadcasting.</summary>
    /// <summary>Raised with the loaded (but not yet started) session, the local player, the live
    /// connection, and the first player the lobby's dice roll settled on (a PlayerNumber).</summary>
    public event Action<GameSession, Player, GameConnection, int>? GameStarted;

    public event Action? BackRequested;

    public OnlineLobbyViewModel(GrandArchiveApiClient apiClient, DeckStorageService deckStorage, GameStorageService gameStorage, PlayerNameStorageService playerNameStorage)
    {
        _apiClient = apiClient;
        _deckStorage = deckStorage;
        _gameStorage = gameStorage;
        _playerNameStorage = playerNameStorage;
        _playerName = playerNameStorage.Load() ?? Environment.UserName;
    }

    [RelayCommand]
    private async Task Host()
    {
        ErrorMessage = null;
        IsHost = true;
        IsConnecting = true;

        _connection = new GameConnection();
        _connection.MessageReceived += OnMessageReceived;
        _connection.Disconnected += OnDisconnected;

        var localIp = GameConnection.GetLocalIPAddress() ?? "127.0.0.1";
        HostAddress = $"{localIp}:{DefaultPort}";

        try
        {
            await _connection.HostAsync(DefaultPort);
            await OnConnectedAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't host: {ex.Message}";
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task Join()
    {
        ErrorMessage = null;
        if (!TryParseAddress(JoinAddress, out var host, out var port))
        {
            ErrorMessage = "Enter an address like 192.168.1.5:51900.";
            return;
        }

        IsHost = false;
        IsConnecting = true;

        _connection = new GameConnection();
        _connection.MessageReceived += OnMessageReceived;
        _connection.Disconnected += OnDisconnected;

        try
        {
            await _connection.JoinAsync(host, port);
            await OnConnectedAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't connect: {ex.Message}";
            IsConnecting = false;
        }
    }

    private DispatcherTimer? _copyNotificationTimer;

    [ObservableProperty]
    private string? _copyStatusMessage;

    [RelayCommand]
    private void CopyHostAddress()
    {
        if (string.IsNullOrEmpty(HostAddress))
        {
            return;
        }

        Clipboard.SetText(HostAddress);
        CopyStatusMessage = "Copied!";

        _copyNotificationTimer?.Stop();
        _copyNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copyNotificationTimer.Tick += (_, _) =>
        {
            CopyStatusMessage = null;
            _copyNotificationTimer!.Stop();
        };
        _copyNotificationTimer.Start();
    }

    private async Task OnConnectedAsync()
    {
        IsConnecting = false;
        IsConnected = true;
        await _connection!.SendAsync(new OnlineMessage
        {
            Kind = OnlineMessageKind.Hello,
            PlayerName = PlayerName.Trim(),
            AppVersion = AppVersion.Display,
        });
    }

    private static bool TryParseAddress(string input, out string host, out int port)
    {
        port = DefaultPort;
        var trimmed = input.Trim();
        var parts = trimmed.Split(':', 2);
        host = parts[0];
        if (parts.Length == 2 && int.TryParse(parts[1], out var parsedPort))
        {
            port = parsedPort;
        }

        return host.Length > 0;
    }

    [RelayCommand]
    private async Task ToggleReady()
    {
        IsReady = !IsReady;
        await _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.Ready, Ready = IsReady });
    }

    /// <summary>Disabled once you've rolled, so you can't just keep re-rolling until you win — the
    /// one exception is a tie, which re-enables Roll for both sides so the tie can be broken.</summary>
    private bool CanRollDice() => !MyDiceTotal.HasValue || IsDiceTied;

    [RelayCommand(CanExecute = nameof(CanRollDice))]
    private async Task RollDice()
    {
        var random = new Random();
        var first = random.Next(1, 7);
        var second = random.Next(1, 7);

        MyDice.Clear();
        MyDice.Add(new DieViewModel(first, TimeSpan.Zero) { FaceValue = first, IsSettled = true });
        MyDice.Add(new DieViewModel(second, TimeSpan.Zero) { FaceValue = second, IsSettled = true });
        MyDiceTotal = first + second;

        await _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.DiceRoll, Die1 = first, Die2 = second });
    }

    /// <summary>Only enabled for whoever rolled strictly higher (CanChooseFirstPlayer) — the choice
    /// itself is still just a broadcast Int the other side displays and highlights, not enforced.</summary>
    [RelayCommand(CanExecute = nameof(CanChooseFirstPlayer))]
    private Task ChooseMeFirst() => ChooseFirstPlayerAsync(chooseMe: true);

    [RelayCommand(CanExecute = nameof(CanChooseFirstPlayer))]
    private Task ChooseOpponentFirst() => ChooseFirstPlayerAsync(chooseMe: false);

    private async Task ChooseFirstPlayerAsync(bool chooseMe)
    {
        var myNumber = IsHost ? 0 : 1;
        var opponentNumber = IsHost ? 1 : 0;
        FirstPlayerNumber = chooseMe ? myNumber : opponentNumber;
        await _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.TurnOrderChoice, FirstPlayerNumber = FirstPlayerNumber.Value });
    }

    [RelayCommand(CanExecute = nameof(CanStartGame))]
    private async Task StartGame()
    {
        if (FirstPlayerNumber is not int firstPlayerNumber)
        {
            return;
        }

        var started = await TryBuildAndStartAsync(firstPlayerNumber);
        if (started)
        {
            await _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.StartGame, FirstPlayerNumber = firstPlayerNumber });
        }
    }

    private bool CanStartGame() =>
        IsHost && IsConnected && IsReady && OpponentReady && FirstPlayerNumber.HasValue && VersionMismatchMessage is null;

    private void OnMessageReceived(OnlineMessage message)
    {
        switch (message.Kind)
        {
            case OnlineMessageKind.Hello:
                OpponentName = message.PlayerName;
                _opponentHasGreeted = true;
                OpponentVersion = message.AppVersion;
                // Raised by hand too: a build too old to send a version leaves OpponentVersion at
                // null (no change notification), but the greeting itself changes the answer.
                OnPropertyChanged(nameof(VersionMismatchMessage));
                StartGameCommand.NotifyCanExecuteChanged();
                break;
            case OnlineMessageKind.Ready:
                OpponentReady = message.Ready;
                break;
            case OnlineMessageKind.DiceRoll:
                OpponentDice.Clear();
                OpponentDice.Add(new DieViewModel(message.Die1, TimeSpan.Zero) { FaceValue = message.Die1, IsSettled = true });
                OpponentDice.Add(new DieViewModel(message.Die2, TimeSpan.Zero) { FaceValue = message.Die2, IsSettled = true });
                OpponentDiceTotal = message.Die1 + message.Die2;
                break;
            case OnlineMessageKind.TurnOrderChoice:
                FirstPlayerNumber = message.FirstPlayerNumber;
                break;
            case OnlineMessageKind.StartGame:
                _ = TryBuildAndStartAsync(message.FirstPlayerNumber);
                break;
        }
    }

    // Dropped before the opponent ever said hello usually means a build too old to speak this
    // version's wire format at all (its messages can't even be read), not a flaky network.
    private void OnDisconnected() => ErrorMessage = _opponentHasGreeted
        ? "Connection lost."
        : $"Connection lost before your opponent connected fully. If they're on an older version, you both need the same one (yours is {AppVersion.Display}).";

    /// <summary>
    /// Builds this side's own local, 2-player GameSession and raises GameStarted. Host and guest
    /// each build their own independent session (nobody sends game state across for this) — but both
    /// add players in the same host-then-guest order, so PlayerNumber (0 = host, 1 = guest) means the
    /// same physical player on both ends. Only the local player's deck is ever loaded here; the
    /// opponent's Player starts empty and fills in once their own PlayerState broadcast arrives —
    /// see DeckSessionBuilder.LoadDeckIntoPlayer's own doc comment.
    /// </summary>
    private async Task<bool> TryBuildAndStartAsync(int firstPlayerNumber)
    {
        var activeDeck = _deckStorage.GetActiveDeck();
        if (activeDeck is null)
        {
            ErrorMessage = "No active deck — go back and pick one from Change Deck first.";
            return false;
        }

        _playerNameStorage.Save(PlayerName.Trim());

        List<DeckSessionBuilder.Entry> entries;
        try
        {
            entries = await DeckSessionBuilder.ResolveEntriesAsync(activeDeck, _apiClient);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load your deck: {ex.Message}";
            return false;
        }

        var session = new GameSession();
        Player me;
        Player opponent;
        if (IsHost)
        {
            me = session.AddPlayer(PlayerName.Trim());
            opponent = session.AddPlayer(OpponentName ?? "Opponent");
        }
        else
        {
            opponent = session.AddPlayer(OpponentName ?? "Opponent");
            me = session.AddPlayer(PlayerName.Trim());
        }

        // Only loads the deck — the game itself doesn't start here. The board opens on the
        // sideboarding panel, and the game begins (StartNewGameForPlayer, first player, ...) once both
        // players are Ready there and the host hits Start — see GameBoardViewModel's New Game
        // region. firstPlayerNumber (the lobby's dice-roll result) just becomes that panel's default.
        DeckSessionBuilder.LoadDeckIntoPlayer(me, entries);
        session.Shuffle(me, ZoneType.MainDeck);

        _connection!.MessageReceived -= OnMessageReceived;
        _connection.Disconnected -= OnDisconnected;

        GameStarted?.Invoke(session, me, _connection, firstPlayerNumber);
        return true;
    }

    [RelayCommand]
    private void Back()
    {
        _connection?.Dispose();
        BackRequested?.Invoke();
    }
}
