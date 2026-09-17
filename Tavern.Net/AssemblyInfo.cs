using System.Runtime.CompilerServices;

// Lets Tavern.Net.Tests exercise a handful of internal members (e.g. GameSession.
// ApplyRemoteGameState) directly, rather than only through GameBoardViewModel, which would need a
// live GameConnection to construct.
[assembly: InternalsVisibleTo("Tavern.Net.Tests")]
