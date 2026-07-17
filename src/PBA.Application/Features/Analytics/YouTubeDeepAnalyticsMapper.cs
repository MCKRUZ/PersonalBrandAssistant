using System.Globalization;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;

namespace PBA.Application.Features.Analytics;

// Maps a single-dimension Analytics v2 report (columns typed DIMENSION | METRIC) into one labeled series per
// metric column, points labeled by the report's dimension value (day, country, trafficSourceType, ...).
// Section-06's live deep-analytics query wires the client call + this mapper into its DTO. Reports with
// multiple dimensions use the FIRST dimension column as the label; section-06 handles any composite cases.
public static class YouTubeDeepAnalyticsMapper
{
    private const string DimensionType = "DIMENSION";
    private const string MetricType = "METRIC";

    public static IReadOnlyList<YouTubeMetricSeries> MapToSeries(YouTubeReportResult report)
    {
        var columns = report.Columns;
        var labelIndex = FirstIndexOfType(columns, DimensionType);

        var series = new List<YouTubeMetricSeries>();
        for (var col = 0; col < columns.Count; col++)
        {
            if (!IsType(columns[col], MetricType))
                continue;

            var points = new List<YouTubeMetricPoint>();
            foreach (var row in report.Rows)
            {
                var label = labelIndex >= 0 && labelIndex < row.Count ? row[labelIndex] : string.Empty;
                var value = col < row.Count
                    && double.TryParse(row[col], NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                        ? d : 0d;
                points.Add(new YouTubeMetricPoint(label, value));
            }

            series.Add(new YouTubeMetricSeries(columns[col].Name, points));
        }

        return series;
    }

    private static int FirstIndexOfType(IReadOnlyList<YouTubeReportColumn> columns, string columnType)
    {
        for (var i = 0; i < columns.Count; i++)
            if (IsType(columns[i], columnType))
                return i;
        return -1;
    }

    private static bool IsType(YouTubeReportColumn column, string columnType) =>
        string.Equals(column.ColumnType, columnType, StringComparison.OrdinalIgnoreCase);
}
