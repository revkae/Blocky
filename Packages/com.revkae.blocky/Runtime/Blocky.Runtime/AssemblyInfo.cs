using System.Runtime.CompilerServices;

// The tests drive what Unity would call by itself in Play mode (a ticker's Start and Update) through internal seams.
[assembly: InternalsVisibleTo("Blocky.Runtime.Tests")]
