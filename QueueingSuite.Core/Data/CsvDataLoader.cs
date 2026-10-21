using System.Globalization;

namespace QueueingSuite.Core.Data;

/// <summary>
/// Reads both KFC file layouts (header-driven, column order does not matter).
/// <list type="bullet">
/// <item>Tandem: CustomerID, ArrivalMins, Q1StartMins, Q1EndMins, Q2EndMins (extra columns ignored).</item>
/// <item>Simple: CustomerID, ArrivalMinutes, ObservedWaitMinutes, ServiceMinutes, Queue2Minutes (optional).
/// Mapped to Start = Arrival + Wait, End = Start + Service, Q2End = End + Queue2Minutes.</item>
/// </list>
/// Every malformed row is rejected with its line number. Records are returned sorted by arrival time, then ID.
/// </summary>
public static class CsvDataLoader
{
    public static LoadedData Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not find input data CSV: {path}", path);
        return Parse(File.ReadAllLines(path), Path.GetFileName(path));
    }

    /// <summary>Parses CSV text lines (header first). Line numbers in messages are 1-based file lines.</summary>
    public static LoadedData Parse(IReadOnlyList<string> lines, string source = "csv")
    {
        int headerIndex = -1;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Trim().Trim('﻿').Length > 0) { headerIndex = i; break; }
        if (headerIndex < 0) throw new CsvFormatException("The CSV is empty.");

        var headers = lines[headerIndex].Trim('﻿').Split(',').Select(h => h.Trim().Trim('"')).ToArray();
        int Col(params string[] names) =>
            Array.FindIndex(headers, h => names.Any(n => h.Equals(n, StringComparison.OrdinalIgnoreCase)));

        int idCol = Col("CustomerID", "ID");
        int tArrival = Col("ArrivalMins"), tStart = Col("Q1StartMins"), tEnd = Col("Q1EndMins"), tQ2 = Col("Q2EndMins");
        int sArrival = Col("ArrivalMinutes"), sWait = Col("ObservedWaitMinutes"), sService = Col("ServiceMinutes"), sQ2 = Col("Queue2Minutes");

        CsvFormat format;
        if (tArrival >= 0 && tStart >= 0 && tEnd >= 0 && tQ2 >= 0) format = CsvFormat.Tandem;
        else if (sArrival >= 0 && sWait >= 0 && sService >= 0) format = CsvFormat.Simple;
        else
            throw new CsvFormatException(
                "Unrecognised header. Expected either 'CustomerID,ArrivalMins,Q1StartMins,Q1EndMins,Q2EndMins,...' (tandem file) " +
                "or 'CustomerID,ArrivalMinutes,ObservedWaitMinutes,ServiceMinutes[,Queue2Minutes]' (dashboard file).", headerIndex + 1);
        if (idCol < 0) throw new CsvFormatException("Missing the CustomerID column.", headerIndex + 1);

        var needed = format == CsvFormat.Tandem
            ? new[] { idCol, tArrival, tStart, tEnd, tQ2 }
            : new[] { idCol, sArrival, sWait, sService };
        int minColumns = needed.Max() + 1;

        var records = new List<CustomerObservation>();
        var seenIds = new HashSet<int>();
        for (int i = headerIndex + 1; i < lines.Count; i++)
        {
            string raw = lines[i].Trim();
            if (raw.Length == 0) continue;
            int lineNo = i + 1;
            var cells = raw.Split(',');
            if (cells.Length < minColumns)
                throw new CsvFormatException($"expected at least {minColumns} columns but found {cells.Length}.", lineNo);

            int id = ParseInt(cells[idCol], "CustomerID", lineNo);
            if (!seenIds.Add(id)) throw new CsvFormatException($"duplicate CustomerID {id}.", lineNo);

            CustomerObservation obs;
            if (format == CsvFormat.Tandem)
            {
                double arrival = ParseTime(cells[tArrival], "ArrivalMins", lineNo);
                double start = ParseTime(cells[tStart], "Q1StartMins", lineNo);
                double end = ParseTime(cells[tEnd], "Q1EndMins", lineNo);
                double q2 = ParseTime(cells[tQ2], "Q2EndMins", lineNo);
                if (start < arrival || end < start || q2 < end)
                    throw new CsvFormatException($"inconsistent event times for customer {id} (need Arrival <= Q1Start <= Q1End <= Q2End).", lineNo);
                obs = new CustomerObservation(id, arrival, start, end, q2);
            }
            else
            {
                double arrival = ParseTime(cells[sArrival], "ArrivalMinutes", lineNo);
                double wait = ParseTime(cells[sWait], "ObservedWaitMinutes", lineNo);
                double service = ParseTime(cells[sService], "ServiceMinutes", lineNo);
                double q2 = sQ2 >= 0 && sQ2 < cells.Length ? ParseTime(cells[sQ2], "Queue2Minutes", lineNo) : 0;
                double start = arrival + wait, end = start + service;
                obs = new CustomerObservation(id, arrival, start, end, end + q2);
            }
            records.Add(obs);
        }

        if (records.Count == 0) throw new CsvFormatException("The CSV contains no customer rows.");
        return new LoadedData(format, records.OrderBy(r => r.Arrival).ThenBy(r => r.Id).ToList(), source);
    }

    private static int ParseInt(string text, string column, int lineNo) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : throw new CsvFormatException($"'{text.Trim()}' is not a valid integer for {column}.", lineNo);

    private static double ParseTime(string text, string column, int lineNo)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
            throw new CsvFormatException($"'{text.Trim()}' is not a valid number for {column}.", lineNo);
        if (v < 0) throw new CsvFormatException($"{column} must not be negative (got {v}).", lineNo);
        return v;
    }
}
