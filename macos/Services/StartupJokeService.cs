using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace LaptopQATestingMac.Services;

internal static class StartupJokeService
{
    private const string JokeResourceName = "LaptopQATestingMac.Assets.loading-jokes.txt";
    private static readonly string[] Jokes = LoadJokes();

    public static int Count => Jokes.Length;

    public static string Next(string dataRoot)
    {
        if (Jokes.Length == 0) return "Preparing Laptop QA.";

        try
        {
            var runtimeFolder = Path.Combine(dataRoot, ".runtime");
            var statePath = Path.Combine(runtimeFolder, "startup-joke-state.json");
            var legacyStatePath = Path.Combine(runtimeFolder, "startup-joke-index.txt");
            Directory.CreateDirectory(runtimeFolder);

            StartupJokeDeckState? state = null;
            if (File.Exists(statePath))
            {
                try
                {
                    state = JsonSerializer.Deserialize<StartupJokeDeckState>(File.ReadAllText(statePath));
                }
                catch
                {
                    state = null;
                }
            }

            var valid = state is not null &&
                        state.JokeCount == Jokes.Length &&
                        state.Order.Count == Jokes.Length &&
                        state.Order.Distinct().Count() == Jokes.Length &&
                        state.Order.All(index => index >= 0 && index < Jokes.Length) &&
                        state.Position >= 0 &&
                        state.Position <= Jokes.Length;

            if (!valid)
            {
                var previousIndex = -1;
                if (File.Exists(legacyStatePath) &&
                    int.TryParse(File.ReadAllText(legacyStatePath).Trim(), out var legacyNextIndex))
                {
                    previousIndex = (Math.Clamp(legacyNextIndex, 0, Jokes.Length - 1) - 1 + Jokes.Length) % Jokes.Length;
                }
                state = CreateDeck(previousIndex);
            }
            else if (state!.Position >= state.Order.Count)
            {
                state = CreateDeck(state.LastIndex);
            }

            var selectedIndex = state!.Order[state.Position++];
            state.LastIndex = selectedIndex;
            SaveState(statePath, state);
            return Jokes[selectedIndex];
        }
        catch
        {
            return Jokes[Random.Shared.Next(Jokes.Length)];
        }
    }

    private static StartupJokeDeckState CreateDeck(int previousIndex)
    {
        var order = Enumerable.Range(0, Jokes.Length).ToList();
        for (var index = order.Count - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (order[index], order[swapIndex]) = (order[swapIndex], order[index]);
        }

        if (order.Count > 1 && order[0] == previousIndex)
        {
            var swapIndex = order.FindIndex(1, candidate => candidate != previousIndex);
            if (swapIndex > 0)
                (order[0], order[swapIndex]) = (order[swapIndex], order[0]);
        }

        return new StartupJokeDeckState
        {
            JokeCount = Jokes.Length,
            Order = order,
            Position = 0,
            LastIndex = previousIndex
        };
    }

    private static void SaveState(string statePath, StartupJokeDeckState state)
    {
        var json = JsonSerializer.Serialize(state);
        var temporaryPath = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, statePath, true);
        }
        catch
        {
            try
            {
                File.WriteAllText(statePath, json);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private static string[] LoadJokes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(JokeResourceName);
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class StartupJokeDeckState
    {
        public int JokeCount { get; set; }
        public List<int> Order { get; set; } = new();
        public int Position { get; set; }
        public int LastIndex { get; set; } = -1;
    }
}
