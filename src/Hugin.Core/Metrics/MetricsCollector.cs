using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Hugin.Core.Metrics;

/// <summary>
/// Prometheus-compatible metrics collector for the IRC server.
/// </summary>
/// <remarks>
/// This provides observability into server performance and behavior,
/// exposing metrics in Prometheus text format for scraping.
/// </remarks>
public sealed class MetricsCollector
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private readonly ConcurrentDictionary<string, Gauge> _gauges = new();
    private readonly ConcurrentDictionary<string, Histogram> _histograms = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    /// <summary>
    /// Gets or creates a counter metric.
    /// </summary>
    public Counter GetCounter(string name, string help, params string[] labelNames)
    {
        return _counters.GetOrAdd(name, _ => new Counter(name, help, labelNames));
    }

    /// <summary>
    /// Gets or creates a gauge metric.
    /// </summary>
    public Gauge GetGauge(string name, string help, params string[] labelNames)
    {
        return _gauges.GetOrAdd(name, _ => new Gauge(name, help, labelNames));
    }

    /// <summary>
    /// Gets or creates a histogram metric.
    /// </summary>
    public Histogram GetHistogram(string name, string help, double[] buckets, params string[] labelNames)
    {
        return _histograms.GetOrAdd(name, _ => new Histogram(name, help, buckets, labelNames));
    }

    /// <summary>
    /// Gets the server uptime in seconds.
    /// </summary>
    public double UptimeSeconds => _uptime.Elapsed.TotalSeconds;

    /// <summary>
    /// Exports all metrics in Prometheus text format.
    /// </summary>
    public string Export()
    {
        var sb = new StringBuilder();

        // Add uptime metric
        sb.AppendLine("# HELP hugin_uptime_seconds Server uptime in seconds");
        sb.AppendLine("# TYPE hugin_uptime_seconds gauge");
        sb.AppendLine(CultureInfo.InvariantCulture, $"hugin_uptime_seconds {UptimeSeconds:F3}");
        sb.AppendLine();

        // Export counters
        foreach (var counter in _counters.Values)
        {
            sb.Append(counter.Export());
        }

        // Export gauges
        foreach (var gauge in _gauges.Values)
        {
            sb.Append(gauge.Export());
        }

        // Export histograms
        foreach (var histogram in _histograms.Values)
        {
            sb.Append(histogram.Export());
        }

        return sb.ToString();
    }
}

/// <summary>
/// A monotonically increasing counter metric.
/// </summary>
public sealed class Counter
{
    private readonly string _name;
    private readonly string _help;
    private readonly string[] _labelNames;
    private readonly ConcurrentDictionary<string, long> _values = new();

    internal Counter(string name, string help, string[] labelNames)
    {
        _name = name;
        _help = help;
        _labelNames = labelNames;
    }

    /// <summary>
    /// Increments the counter by 1.
    /// </summary>
    public void Inc(params string[] labelValues)
    {
        var key = GetKey(labelValues);
        _values.AddOrUpdate(key, 1, (_, v) => v + 1);
    }

    /// <summary>
    /// Increments the counter by a specified amount.
    /// </summary>
    public void Inc(long amount, params string[] labelValues)
    {
        var key = GetKey(labelValues);
        _values.AddOrUpdate(key, amount, (_, v) => v + amount);
    }

    private static string GetKey(string[] labelValues)
    {
        if (labelValues.Length == 0) return string.Empty;
        return string.Join(",", labelValues);
    }

    internal string Export()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# HELP {_name} {_help}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# TYPE {_name} counter");

        if (_labelNames.Length == 0)
        {
            var value = _values.GetValueOrDefault(string.Empty, 0);
            sb.AppendLine(CultureInfo.InvariantCulture, $"{_name} {value}");
        }
        else
        {
            foreach (var (key, value) in _values)
            {
                var labels = FormatLabels(key);
                sb.AppendLine(CultureInfo.InvariantCulture, $"{_name}{{{labels}}} {value}");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private string FormatLabels(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        var values = key.Split(',');
        var pairs = new List<string>();
        for (int i = 0; i < _labelNames.Length && i < values.Length; i++)
        {
            pairs.Add($"{_labelNames[i]}=\"{EscapeLabel(values[i])}\"");
        }
        return string.Join(",", pairs);
    }

    private static string EscapeLabel(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}

/// <summary>
/// A gauge metric that can go up or down.
/// </summary>
public sealed class Gauge
{
    private readonly string _name;
    private readonly string _help;
    private readonly string[] _labelNames;
    private readonly ConcurrentDictionary<string, double> _values = new();

    internal Gauge(string name, string help, string[] labelNames)
    {
        _name = name;
        _help = help;
        _labelNames = labelNames;
    }

    /// <summary>
    /// Sets the gauge to a specific value.
    /// </summary>
    public void Set(double value, params string[] labelValues)
    {
        var key = GetKey(labelValues);
        _values[key] = value;
    }

    /// <summary>
    /// Increments the gauge by 1.
    /// </summary>
    public void Inc(params string[] labelValues)
    {
        var key = GetKey(labelValues);
        _values.AddOrUpdate(key, 1, (_, v) => v + 1);
    }

    /// <summary>
    /// Decrements the gauge by 1.
    /// </summary>
    public void Dec(params string[] labelValues)
    {
        var key = GetKey(labelValues);
        _values.AddOrUpdate(key, -1, (_, v) => v - 1);
    }

    private static string GetKey(string[] labelValues)
    {
        if (labelValues.Length == 0) return string.Empty;
        return string.Join(",", labelValues);
    }

    internal string Export()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# HELP {_name} {_help}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# TYPE {_name} gauge");

        if (_labelNames.Length == 0)
        {
            var value = _values.GetValueOrDefault(string.Empty, 0);
            sb.AppendLine(CultureInfo.InvariantCulture, $"{_name} {value:G}");
        }
        else
        {
            foreach (var (key, value) in _values)
            {
                var labels = FormatLabels(key);
                sb.AppendLine(CultureInfo.InvariantCulture, $"{_name}{{{labels}}} {value:G}");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private string FormatLabels(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        var values = key.Split(',');
        var pairs = new List<string>();
        for (int i = 0; i < _labelNames.Length && i < values.Length; i++)
        {
            pairs.Add($"{_labelNames[i]}=\"{EscapeLabel(values[i])}\"");
        }
        return string.Join(",", pairs);
    }

    private static string EscapeLabel(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}

/// <summary>
/// A histogram metric for measuring distributions.
/// </summary>
public sealed class Histogram
{
    private readonly string _name;
    private readonly string _help;
    private readonly double[] _buckets;
    private readonly string[] _labelNames;
    private readonly ConcurrentDictionary<string, HistogramData> _values = new();

    internal Histogram(string name, string help, double[] buckets, string[] labelNames)
    {
        _name = name;
        _help = help;
        _buckets = buckets.OrderBy(b => b).ToArray();
        _labelNames = labelNames;
    }

    /// <summary>
    /// Observes a value.
    /// </summary>
    public void Observe(double value, params string[] labelValues)
    {
        var key = GetKey(labelValues);
        var data = _values.GetOrAdd(key, _ => new HistogramData(_buckets.Length));

        Interlocked.Increment(ref data.Count);
        InterlockedAdd(ref data.Sum, value);

        for (int i = 0; i < _buckets.Length; i++)
        {
            if (value <= _buckets[i])
            {
                Interlocked.Increment(ref data.BucketCounts[i]);
            }
        }
    }

    private static void InterlockedAdd(ref double location, double value)
    {
        double newCurrentValue;
        do
        {
            newCurrentValue = location;
        }
        while (Interlocked.CompareExchange(ref location, newCurrentValue + value, newCurrentValue) != newCurrentValue);
    }

    private static string GetKey(string[] labelValues)
    {
        if (labelValues.Length == 0) return string.Empty;
        return string.Join(",", labelValues);
    }

    internal string Export()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# HELP {_name} {_help}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"# TYPE {_name} histogram");

        foreach (var (key, data) in _values)
        {
            var baseLabels = FormatLabels(key);
            var prefix = string.IsNullOrEmpty(baseLabels) ? "" : $"{baseLabels},";

            long cumulative = 0;
            for (int i = 0; i < _buckets.Length; i++)
            {
                cumulative += data.BucketCounts[i];
                sb.AppendLine(CultureInfo.InvariantCulture, $"{_name}_bucket{{{prefix}le=\"{_buckets[i]:G}\"}} {cumulative}");
            }
            sb.AppendLine(CultureInfo.InvariantCulture, $"{_name}_bucket{{{prefix}le=\"+Inf\"}} {data.Count}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"{_name}_sum{{{FormatLabels(key)}}} {data.Sum:G}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"{_name}_count{{{FormatLabels(key)}}} {data.Count}");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private string FormatLabels(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        var values = key.Split(',');
        var pairs = new List<string>();
        for (int i = 0; i < _labelNames.Length && i < values.Length; i++)
        {
            pairs.Add($"{_labelNames[i]}=\"{EscapeLabel(values[i])}\"");
        }
        return string.Join(",", pairs);
    }

    private static string EscapeLabel(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

    private sealed class HistogramData
    {
        public long Count;
        public double Sum;
        public long[] BucketCounts;

        public HistogramData(int bucketCount)
        {
            BucketCounts = new long[bucketCount];
        }
    }
}
