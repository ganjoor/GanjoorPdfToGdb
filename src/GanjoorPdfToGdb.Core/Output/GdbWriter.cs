using Microsoft.Data.Sqlite;

namespace GanjoorPdfToGdb;

/// <summary>
/// Writes a Desktop Ganjoor database (.gdb). The schema is identical to DbBrowser.CreateEmptyDB in the
/// desktop source: cat, poem, poet, verse. Values are passed as parameters, so quotes and any other
/// characters in the OCR text are safe, and everything is written in one transaction.
/// </summary>
public static class GdbWriter
{
    public static void Write(Book book, string path)
    {
        if (File.Exists(path)) File.Delete(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using (var con = new SqliteConnection("Data Source=" + path))
        {
            con.Open();
            using var tx = con.BeginTransaction();

            Exec(con, tx,
                "CREATE TABLE [cat] ([id] INTEGER  PRIMARY KEY NOT NULL,[poet_id] INTEGER  NULL,[text] NVARCHAR(100)  NULL,[parent_id] INTEGER  NULL,[url] NVARCHAR(255)  NULL);");
            Exec(con, tx,
                "CREATE TABLE poem (id INTEGER PRIMARY KEY, cat_id INTEGER, title NVARCHAR(255), url NVARCHAR(255));");
            Exec(con, tx,
                "CREATE TABLE [poet] ([id] INTEGER  PRIMARY KEY NOT NULL,[name] NVARCHAR(20)  NULL,[cat_id] INTEGER  NULL  NULL, [description] TEXT);");
            Exec(con, tx,
                "CREATE TABLE [verse] ([poem_id] INTEGER  NULL,[vorder] INTEGER  NULL,[position] INTEGER  NULL,[text] TEXT  NULL);");

            // the poet's root category and the poet row
            Exec(con, tx, "INSERT INTO cat (id, poet_id, text, parent_id, url) VALUES ($id, $poet, $text, 0, '')",
                ("$id", book.PoetCatId), ("$poet", book.PoetId), ("$text", book.PoetName));
            Exec(con, tx, "INSERT INTO poet (id, name, cat_id, description) VALUES ($id, $name, $cat, '')",
                ("$id", book.PoetId), ("$name", book.PoetName), ("$cat", book.PoetCatId));

            foreach (var c in book.Categories)
            {
                Exec(con, tx, "INSERT INTO cat (id, poet_id, text, parent_id, url) VALUES ($id, $poet, $text, $parent, '')",
                    ("$id", c.Id), ("$poet", book.PoetId), ("$text", c.Title), ("$parent", book.PoetCatId));

                foreach (var p in c.Poems)
                {
                    Exec(con, tx, "INSERT INTO poem (id, cat_id, title, url) VALUES ($id, $cat, $title, '')",
                        ("$id", p.Id), ("$cat", c.Id), ("$title", p.Title));

                    foreach (var v in p.Verses)
                        Exec(con, tx, "INSERT INTO verse (poem_id, vorder, position, text) VALUES ($poem, $order, $pos, $text)",
                            ("$poem", p.Id), ("$order", v.Order), ("$pos", (int)v.Position), ("$text", v.Text));
                }
            }

            tx.Commit();
        }
        SqliteConnection.ClearAllPools(); // release the file lock (matters on Windows)
    }

    private static void Exec(SqliteConnection con, SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
