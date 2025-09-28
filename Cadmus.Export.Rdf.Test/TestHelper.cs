using CsvHelper;
using Fusi.DbManager.PgSql;
using Npgsql;
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Cadmus.Export.Rdf.Test;

/// <summary>
/// Helper class used to seed the PostgreSQL database for testing.
/// </summary>
internal static class TestHelper
{
    private const string DB_NAME = "cadmus-rdf-test";
    private static readonly NpgsqlConnection _connection = new(
        $"Host=localhost;Username=postgres;Password=postgres;Database={DB_NAME}");

    private static string LoadSchema()
    {
        using Stream stream = typeof(TestHelper).Assembly
            .GetManifestResourceStream(
                "Cadmus.Export.Rdf.Test.Assets.Schema.sql")!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    public static void DropDatabase()
    {
        // drop the test database if it exists
        PgSqlDbManager manager = new(_connection.ConnectionString);
        if (manager.Exists(DB_NAME)) manager.RemoveDatabase(DB_NAME);
    }

    private static void EnsureDatabase()
    {
        // ensure the test database exists
        PgSqlDbManager manager = new(_connection.ConnectionString);
        if (!manager.Exists(DB_NAME)) manager.CreateDatabase(DB_NAME,
            LoadSchema(), null);

        if (_connection.State != ConnectionState.Open)
            _connection.Open();
    }

    public static void SeedData(TextReader reader)
    {
        EnsureDatabase();

        // prepare commands
        using NpgsqlCommand nsCmd = new(
            "INSERT INTO namespace_lookup (id, uri) " +
            "VALUES (@id, @uri);", _connection);
        nsCmd.Parameters.Add(new NpgsqlParameter("@id", DbType.String));
        nsCmd.Parameters.Add(new NpgsqlParameter("@uri", DbType.String));

        using NpgsqlCommand nodeCmd = new(
            "INSERT INTO node (id, is_class, tag, \"label\", source_type, sid) " +
            "VALUES (@id, @is_class, @tag, @label, @source_type, @sid);",
            _connection);
        nodeCmd.Parameters.Add(new NpgsqlParameter("@id", DbType.Int32));
        nodeCmd.Parameters.Add(new NpgsqlParameter("@is_class", DbType.Boolean));
        nodeCmd.Parameters.Add(new NpgsqlParameter("@tag", DbType.String));
        nodeCmd.Parameters.Add(new NpgsqlParameter("@label", DbType.String));
        nodeCmd.Parameters.Add(new NpgsqlParameter("@source_type", DbType.Int32));
        nodeCmd.Parameters.Add(new NpgsqlParameter("@sid", DbType.String));

        using NpgsqlCommand tripleCmd = new(
            @"INSERT INTO triple
            (id, s_id, p_id, o_id, o_lit, o_lit_type, o_lit_lang, o_lit_ix,
             o_lit_n, sid, tag)
            VALUES
            (@id, @s_id, @p_id, @o_id, @o_lit, @o_lit_type, @o_lit_lang, @o_lit_ix,
            @o_lit_n, @sid, @tag);", _connection);
        tripleCmd.Parameters.Add(new NpgsqlParameter("@id", DbType.Int32));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@s_id", DbType.String));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@p_id", DbType.String));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@o_id", DbType.String));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@o_lit", DbType.String));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@o_lit_type", DbType.String));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@o_lit_lang", DbType.String));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@o_lit_ix", DbType.Int32));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@o_lit_n", DbType.Int32));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@sid", DbType.String));
        tripleCmd.Parameters.Add(new NpgsqlParameter("@tag", DbType.String));

        CsvReader csv = new(reader, CultureInfo.InvariantCulture);
        string? currentSection = null;
        Regex nodeUriRegex = new(@"^[^:]+:", RegexOptions.Compiled);
        int nNode = 1, nTriple = 1;

        while (csv.Read())
        {
            string? f0 = csv.GetField(0);
            if (f0 == null || string.IsNullOrWhiteSpace(f0)) continue;
            if (f0.StartsWith('#'))
            {
                currentSection = f0[1..].ToLowerInvariant();
                continue;
            }

            switch (currentSection)
            {
                case "namespaces":
                    {
                        string id = f0;
                        string uri = csv.GetField(1) ?? "";
                        nsCmd.Parameters["@id"].Value = id;
                        nsCmd.Parameters["@uri"].Value = uri;
                        nsCmd.ExecuteNonQuery();
                    }
                    break;

                case "nodes":
                    {
                        string uri = f0;
                        nodeCmd.Parameters["@id"].Value = nNode++;
                        nodeCmd.Parameters["@is_class"].Value = false;
                        nodeCmd.Parameters["@tag"].Value = DBNull.Value;
                        nodeCmd.Parameters["@label"].Value = uri;
                        nodeCmd.Parameters["@source_type"].Value = 0;
                        nodeCmd.Parameters["@sid"].Value = $"sid/{uri}";
                        nodeCmd.ExecuteNonQuery();
                    }
                    break;

                case "triples":
                    {
                        string s_id = f0;
                        string p_id = csv.GetField(1) ?? "";
                        string o_field = csv.GetField(2) ?? "";

                        bool isOid = nodeUriRegex.IsMatch(o_field);
                        tripleCmd.Parameters["@s_id"].Value = s_id;
                        tripleCmd.Parameters["@p_id"].Value = p_id;
                        tripleCmd.Parameters["@sid"].Value = $"sid/triple/{nTriple++}";
                        tripleCmd.Parameters["@tag"].Value = DBNull.Value;

                        if (isOid)
                        {
                            tripleCmd.Parameters["@o_id"].Value = o_field;
                            tripleCmd.Parameters["@o_lit"].Value = DBNull.Value;
                            tripleCmd.Parameters["@o_lit_type"].Value = DBNull.Value;
                            tripleCmd.Parameters["@o_lit_lang"].Value = DBNull.Value;
                            tripleCmd.Parameters["@o_lit_ix"].Value = DBNull.Value;
                            tripleCmd.Parameters["@o_lit_n"].Value = DBNull.Value;
                        }
                        else
                        {
                            tripleCmd.Parameters["@o_id"].Value = DBNull.Value;
                            tripleCmd.Parameters["@o_lit"].Value = o_field;
                            tripleCmd.Parameters["@o_lit_type"].Value = DBNull.Value;
                            tripleCmd.Parameters["@o_lit_lang"].Value =
                                string.IsNullOrEmpty(o_field)
                                ? DBNull.Value
                                : "en";
                            tripleCmd.Parameters["@o_lit_ix"].Value = DBNull.Value;
                            tripleCmd.Parameters["@o_lit_n"].Value = DBNull.Value;
                        }
                        tripleCmd.ExecuteNonQuery();
                    }
                    break;
            }
        }

        _connection.Close();
    }
}
