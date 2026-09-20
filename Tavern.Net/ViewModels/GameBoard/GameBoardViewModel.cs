using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;
using Tavern.Net.Online;

namespace Tavern.Net.ViewModels;

/// <summary>The board's view model. It is split across the files in this folder by feature — each
/// is a <c>partial</c> of this class. This file: Fields, core state, construction and the elapsed-time clock.</summary>
public sealed partial class GameBoardViewModel : ObservableObject, IKeyboardShortcutHandler
{
    private readonly GameSession _session;
    private readonly GrandArchiveApiClient _apiClient;
    private readonly GameStorageService _gameStorage;
    private readonly GameConnection? _connection;

    // Local to this game's lifetime: the opponent's cards repeat heavily across every incoming
    // PlayerState broadcast, so this avoids re-awaiting GetCardBySlugAsync for a slug already seen
    // — same reasoning as GameSessionSerializer's own per-restore cache, just kept alive for as
    // long as the connection is (many broadcasts), not just one load.
    private readonly Dictionary<string, CardDto> _opponentCardCache = new();

    private readonly Random _diceRandom = new();

    // Set by the 'N' handler when StartNewGame hands back cards to glimpse instead of a normal
    // opening hand; consumed in MoveGlimpseCard the instant that glimpse finishes, drawing the
    // hand StartNewGame skipped in favor of the glimpse.
    private bool _drawAfterGlimpsing;

    // Drives the face-cycling animation for every die in Dice at once — a single shared clock
    // rather than one timer per die, since they all just need "how much time has elapsed" to know
    // whether they've reached their own (independently randomized) StopAt yet.
    private DispatcherTimer? _diceAnimationTimer;
    private TimeSpan _diceAnimationElapsed;

    public Player Player { get; }

    public GameStats Stats => Player.Stats;

    /// <summary>The opponent's mirrored Player in an online game — kept up to date by ApplyOpponentStateAsync
    /// every time a PlayerState broadcast arrives. Null for solo.</summary>
    public Player? OpponentPlayer { get; }

    public bool IsOnline => _connection is not null;

    /// <summary>The inverse of IsOnline — for XAML bindings that hide something online-only (the
    /// damage-dealt tally makes no sense once there's a real opponent whose Life is directly
    /// visible; see the header's own binding).</summary>
    public bool IsSolo => !IsOnline;

    /// <summary>Set once the connection drops — shown as a banner rather than forcing navigation
    /// away, so the player can finish looking at the board before backing out via Menu themselves.</summary>
    [ObservableProperty]
    private string? _connectionLostMessage;

    public ZoneViewModel Hand { get; }
    public ZoneViewModel Field { get; }
    public ZoneViewModel MainDeck { get; }
    public ZoneViewModel MaterialDeck { get; }
    public ZoneViewModel Graveyard { get; }
    public ZoneViewModel Banishment { get; }
    public ZoneViewModel Memory { get; }
    public ZoneViewModel Champion { get; }
    public ZoneViewModel Tokens { get; }

    /// <summary>Private, fully interactive like Hand (drag in/out, zoomable) — hidden from the
    /// opponent (see OpponentSealedCount, the Opponent panel's own count-only view of it). Cards a
    /// player has sealed away themselves, or received from the opponent via Give (see GameSession.
    /// ReceiveGivenCard).</summary>
    public ZoneViewModel Sealed { get; }

    /// <summary>Mirrors <see cref="GameSession.CurrentPhase"/> so the board can bind to it.</summary>
    [ObservableProperty]
    private TurnPhase _currentPhase;

    /// <summary>The online match clock — ticks once a second while genuinely live (IsOnline and not
    /// yet frozen), starts from a loaded save's own frozen value when viewing one later (see the
    /// constructor's loadedElapsedTime param), and is otherwise irrelevant to solo. See
    /// ShowElapsedTime for when it's actually displayed, and FreezeElapsedTime/ConfirmSaveGame for
    /// how saving stops it.</summary>
    [ObservableProperty]
    private TimeSpan _elapsedTime;

    /// <summary>Strictly online: true for a live game (IsOnline) or when viewing a saved game that
    /// was online (a non-null loadedElapsedTime was passed in) — a solo game never shows this at
    /// all, live or loaded.</summary>
    public bool ShowElapsedTime => IsOnline || _isElapsedTimeFrozen;

    // Set immediately if constructed with a loaded save's own frozen ElapsedTime (never ticks in
    // that case — there's no live connection driving it anyway) and permanently once ConfirmSaveGame
    // successfully saves a live online game (that save's own copy of ElapsedTime is what's frozen;
    // this stops the live display from drifting past it for the rest of this process).
    private bool _isElapsedTimeFrozen;
    private DispatcherTimer? _elapsedTimeTimer;

    /// <summary>Raised when the player wants to return to the start menu, leaving this game running
    /// in the background (nothing is auto-saved — see Save Game).</summary>
    public event Action? BackToMenuRequested;

    public GameBoardViewModel(
        GameSession session,
        Player player,
        GrandArchiveApiClient apiClient,
        GameStorageService gameStorage,
        GameConnection? connection = null,
        Player? opponentPlayer = null,
        TimeSpan? loadedElapsedTime = null,
        bool isFreshSoloGame = false,
        int? onlineFirstPlayerNumber = null)
    {
        _session = session;
        _apiClient = apiClient;
        _gameStorage = gameStorage;
        _connection = connection;
        Player = player;
        Player.PropertyChanged += OnOwnCounterChanged;
        Player.Stats.PropertyChanged += OnOwnCounterChanged;
        OpponentPlayer = opponentPlayer;
        _currentPhase = session.CurrentPhase;
        // Before an online game has started nobody is active yet: treat it as "mine" so that the
        // player who ends up NOT going first sees the turn-handoff transition (and the opponent panel
        // opening with it) once the game actually starts — see UpdateIsMyTurn.
        _isMyTurn = !IsOnline || session.ActivePlayer is null || session.ActivePlayer == player;

        if (loadedElapsedTime is { } elapsed)
        {
            ElapsedTime = elapsed;
            _isElapsedTimeFrozen = true;
        }
        else if (IsOnline)
        {
            _elapsedTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimeTimer.Tick += (_, _) => ElapsedTime += TimeSpan.FromSeconds(1);
            _elapsedTimeTimer.Start();
        }

        // Whoever isn't going first should see this open the instant the game starts, not only on
        // the first turn handoff — UpdateIsMyTurn only fires on a transition, and there isn't one
        // yet this early, so it's set directly here instead.
        _isOpponentPanelOpen = IsOnline && !_isMyTurn;

        if (_connection is not null)
        {
            _connection.BroadcastTick += OnBroadcastTick;
            _connection.MessageReceived += OnMessageReceived;
            _connection.Disconnected += OnConnectionLost;

            // My own MajorEvents feed the merged log too, not just the opponent's (see
            // RefreshMergedLog) — this is the one side of that merge that isn't already covered by
            // RefreshOpponentPanel running on every incoming broadcast.
            Player.Stats.MajorEvents.CollectionChanged += (_, _) => RefreshMergedLog();
        }
        else
        {
            // A loaded save of an online game: both logs are already complete and nothing will
            // ever refresh them, so build the merged log once.
            RefreshMergedLog();
        }

        Hand = new ZoneViewModel(player.GetZone(ZoneType.Hand), apiClient, this);
        Field = new ZoneViewModel(player.GetZone(ZoneType.Field), apiClient, this);
        MainDeck = new ZoneViewModel(player.GetZone(ZoneType.MainDeck), apiClient, this);
        MaterialDeck = new ZoneViewModel(player.GetZone(ZoneType.MaterialDeck), apiClient, this);
        Graveyard = new ZoneViewModel(player.GetZone(ZoneType.Graveyard), apiClient, this);
        Banishment = new ZoneViewModel(player.GetZone(ZoneType.Banishment), apiClient, this);
        Memory = new ZoneViewModel(player.GetZone(ZoneType.Memory), apiClient, this);
        Champion = new ZoneViewModel(player.GetZone(ZoneType.Champion), apiClient, this);
        Tokens = new ZoneViewModel(player.GetZone(ZoneType.Tokens), apiClient, this);
        Sealed = new ZoneViewModel(player.GetZone(ZoneType.Sealed), apiClient, this);

        // Populated once here, not by GameSession.StartNewGame — the Tokens zone is a static
        // catalog, not deck content, and StartNewGame deliberately leaves it untouched (see its
        // own comment) so it survives every subsequent "New Game" reset without refetching.
        if (player.GetZone(ZoneType.Tokens).Cards.Count == 0)
        {
            _ = LoadTokenCatalogAsync();
        }

        // A brand-new solo game (fresh from deck import, not a loaded save) — start it right away
        // rather than leaving the player staring at an empty board until they remember to press
        // 'N' themselves. Loaded saves and online games (which start via their own lobby/session
        // flow) don't pass this.
        if (isFreshSoloGame)
        {
            OpenSideboardPanel(canCancel: false);
        }

        // A brand-new online game (straight from the lobby): sideboard first, and the game starts
        // once both players are Ready and the host hits Start. Defaults the first-player pick to the
        // lobby's dice-roll result.
        if (onlineFirstPlayerNumber is { } lobbyFirstPlayer)
        {
            OpenNewGameReadyUp(canCancel: false, firstPlayerNumber: lobbyFirstPlayer);
        }
    }

    private async Task LoadTokenCatalogAsync()
    {
        var tokens = await _apiClient.GetTokensAsync();

        // Pin tokens this deck's own cards can actually summon (per the API's referenced_by
        // links) to the front, so the common case doesn't mean scrolling the whole catalog.
        // Deck cards are already loaded into Main/Material by DeckImportViewModel.StartGame
        // before GameBoardViewModel is ever constructed, so this reads real names, not a stub.
        var deckCardNames = Player.GetZone(ZoneType.MainDeck).Cards
            .Concat(Player.GetZone(ZoneType.MaterialDeck).Cards)
            .Select(c => c.Card.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordered = tokens.OrderByDescending(token => token.ReferencedBy.Any(r => deckCardNames.Contains(r.Name)));

        var tokenZone = Player.GetZone(ZoneType.Tokens);
        foreach (var token in ordered)
        {
            tokenZone.Cards.Add(new CardInstance(token, ZoneType.Tokens));
        }
    }

    [RelayCommand]
    private void BackToMenu()
    {
        _connection?.Dispose();
        BackToMenuRequested?.Invoke();
    }
}
