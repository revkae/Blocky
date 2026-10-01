using System.Runtime.CompilerServices;

// The tests build the workspace and run its frames outside Play mode, where Unity calls neither Awake nor Update.
[assembly: InternalsVisibleTo("Blocky.Game.Tests")]
