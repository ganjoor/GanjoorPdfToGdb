using Microsoft.Data.Sqlite;

namespace GanjoorPdfToGdb;

public sealed class SitePoem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Category titles from the poet down to the poem's own category.</summary>
    public List<string> CategoryPath { get; } = new();
    public List<SiteVerse> Verses { get; } = new();

    /// <summary>The title ganjoor.net shows: site name » categories » poem title.</summary>
    public string FullTitle => "گنجور » " + string.Join(" » ", CategoryPath.Append(Title));

    /// <summary>The address without the site name (what the ganjoor API expects), e.g. /taleb/divan/ghazal/sh1</summary>
    public string RelativeUrl
    {
        get
        {
            string u = Url.Trim();
            int i = u.IndexOf("://", StringComparison.Ordinal);
            if (i >= 0)
            {
                int slash = u.IndexOf('/', i + 3);
                u = slash >= 0 ? u[slash..] : "/";
            }
            return u;
        }
    }
}

public sealed record SiteVerse(int Order, int Position, string Text);

/// <summary>Reads a Desktop Ganjoor database (.gdb) exported from ganjoor.net.</summary>
public static class GdbReader
{
    public static List<SitePoem> ReadPoems(string gdbPath)
    {
        var cats = new Dictionary<int, (string Text, int Parent)>();
        var poems = new List<SitePoem>();
        var byId = new Dictionary<int, SitePoem>();
        var poemCat = new Dictionary<int, int>();

        using var con = new SqliteConnection("Data Source=" + gdbPath);
        con.Open();

        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT id, text, parent_id FROM cat";
            using var r = cmd.ExecuteReader();
            while (r.Read()) cats[r.GetInt32(0)] = (r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? 0 : r.GetInt32(2));
        }

        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT id, cat_id, title, url FROM poem ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var p = new SitePoem { Id = r.GetInt32(0), Title = r.IsDBNull(2) ? "" : r.GetString(2), Url = r.IsDBNull(3) ? "" : r.GetString(3) };
                poemCat[p.Id] = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                poems.Add(p);
                byId[p.Id] = p;
            }
        }

        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT poem_id, vorder, position, text FROM verse ORDER BY poem_id, vorder";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (byId.TryGetValue(r.GetInt32(0), out var p))
                    p.Verses.Add(new SiteVerse(r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3) ? "" : r.GetString(3)));
        }

        foreach (var p in poems)
        {
            var path = new List<string>();
            int c = poemCat[p.Id];
            for (int guard = 0; c != 0 && cats.TryGetValue(c, out var cat) && guard < 50; guard++)
            {
                path.Add(cat.Text);
                c = cat.Parent;
            }
            path.Reverse();
            p.CategoryPath.AddRange(path);
        }
        return poems;
    }
}
