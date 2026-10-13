using System.Xml.Linq;
using DeltaDebugging.Core;

namespace DeltaDebugging.Cli;

public static class SvgRenderer
{
    private const int PageSize = 60;
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
    public static void Write(SearchResult result, string directory)
    {
        int pages = (result.Trace.Count + PageSize - 1) / PageSize;
        for (int page = 0; page < pages; page++)
        {
            var attempts = result.Trace.Skip(page * PageSize).Take(PageSize).ToArray();
            int height = 160 + attempts.Length * 96;
            var root = new XElement(Ns + "svg", new XAttribute("viewBox", $"0 0 1100 {height}"),
                new XAttribute("width", 1100), new XAttribute("height", height),
                new XElement(Ns + "title", "Delta debugging reduction attempts"),
                new XElement(Ns + "desc", "Chronological attempt tree. Lines connect each attempt to its source candidate. Thick lines indicate accepted reductions."),
                new XElement(Ns + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", "#f8fafc")));
            root.Add(Text(28, 34, $"DELTA DEBUGGING / {result.Metrics.OriginalCount} → {result.Metrics.MinimizedCount} operations", 23));
            root.Add(Text(28, 62, $"Page {page + 1}/{pages} · {(result.Complete ? "Search complete" : "Search incomplete")} · retained positions shown in each strip", 14));
            root.Add(Text(28, 86, "Red: target failure / Green: pass / Gray: unresolved / Amber: other failure", 14));
            if (page > 0) root.Add(Link(28, 113, "← Previous page", FileName(page - 1)));
            if (page + 1 < pages) root.Add(Link(850, 113, "Next page →", FileName(page + 1)));
            for (int row = 0; row < attempts.Length; row++)
            {
                var attempt = attempts[row];
                int y = 134 + row * 96;
                int parentRow = attempt.ParentId is int id ? Array.FindIndex(attempts, a => a.Id == id) : -1;
                if (parentRow >= 0)
                {
                    int lane = 32 + (attempt.ParentId!.Value % 5) * 14;
                    root.Add(new XElement(Ns + "path", new XAttribute("d", $"M 140 {168 + parentRow * 96} H {lane} V {y + 34} H 140"),
                        new XAttribute("fill", "none"), new XAttribute("stroke", attempt.Accepted ? "#b91c1c" : "#94a3b8"),
                        new XAttribute("stroke-width", attempt.Accepted ? 3 : 1)));
                }
                string color = attempt.Observation.Outcome switch
                {
                    Outcome.TargetFailure => "#fee2e2", Outcome.Pass => "#dcfce7",
                    Outcome.OtherFailure => "#fef3c7", _ => "#e2e8f0"
                };
                var group = new XElement(Ns + "g",
                    new XElement(Ns + "title", $"Indices: {string.Join(", ", attempt.Indices)}; signature: {attempt.Observation.Signature}"),
                    new XElement(Ns + "rect", new XAttribute("x", 140), new XAttribute("y", y),
                        new XAttribute("width", 930), new XAttribute("height", 82), new XAttribute("rx", 8),
                        new XAttribute("fill", color), new XAttribute("stroke", attempt.Accepted ? "#b91c1c" : "#cbd5e1")),
                    Text(156, y + 22, $"#{attempt.Id} {attempt.Kind} · {attempt.Indices.Length} operations · {attempt.Observation.Outcome}" +
                        (attempt.CacheHit ? " · CACHE HIT" : "") + (attempt.Accepted ? " · ACCEPTED" : ""), 15),
                    Text(156, y + 43, attempt.ParentId is int parent ? $"Source candidate #{parent}" : "Original failing input", 12));
                const int bins = 100;
                var occupied = attempt.Indices.Select(i => (int)((long)i * bins / Math.Max(1, result.Metrics.OriginalCount))).ToHashSet();
                for (int bin = 0; bin < bins; bin++)
                    group.Add(new XElement(Ns + "rect", new XAttribute("x", 156 + bin * 9), new XAttribute("y", y + 55),
                        new XAttribute("width", 8), new XAttribute("height", 12),
                        new XAttribute("fill", occupied.Contains(bin) ? "#334155" : "#ffffff")));
                root.Add(group);
            }
            new XDocument(root).Save(Path.Combine(directory, FileName(page)));
        }
    }

    private static string FileName(int page) => page == 0 ? "reduction.svg" : $"reduction-{page + 1:D3}.svg";
    private static XElement Text(int x, int y, string text, int size) => new(Ns + "text",
        new XAttribute("x", x), new XAttribute("y", y), new XAttribute("font-family", "Segoe UI, sans-serif"),
        new XAttribute("font-size", size), new XAttribute("fill", "#0f172a"), text);
    private static XElement Link(int x, int y, string label, string file) => new(Ns + "a", new XAttribute("href", file), Text(x, y, label, 14));
}
