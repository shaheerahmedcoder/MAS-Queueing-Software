using MathNet.Numerics.Distributions;

namespace QueueingSuite.Core.Simulation;

/// <summary>
/// A random-variate generator for inter-arrival or service times. All variates are produced by explicit,
/// explainable algorithms (inverse transform, Box-Muller, Marsaglia-Tsang) from a seeded <see cref="Random"/>.
/// </summary>
public interface IDistribution
{
    string Name { get; }
    /// <summary>Exact mean of the variates this generator produces.</summary>
    double Mean { get; }
    /// <summary>Exact variance of the variates this generator produces.</summary>
    double Variance { get; }
    /// <summary>Squared coefficient of variation Var/Mean^2 (0 when the mean is 0).</summary>
    double Cv2 { get; }
    double Sample(Random rng);
    /// <summary>True when <see cref="Cdf"/> is a closed-form theoretical CDF (needed by chi-square generator tests).</summary>
    bool HasCdf { get; }
    double Cdf(double x);
    /// <summary>Rewinds stateful generators (trace replay) so a run is repeatable. No-op for random distributions.</summary>
    void Reset();
}

public abstract class DistributionBase : IDistribution
{
    public abstract string Name { get; }
    public abstract double Mean { get; }
    public abstract double Variance { get; }
    public double Cv2 => Mean > 0 ? Variance / (Mean * Mean) : 0;
    public abstract double Sample(Random rng);
    public virtual bool HasCdf => true;
    public abstract double Cdf(double x);
    public virtual void Reset() { }
    public override string ToString() => Name;

    /// <summary>Uniform(0,1) strictly below 1 so that 1-U is in (0,1] and ln() is finite.</summary>
    protected static double U(Random rng) => rng.NextDouble();
}

/// <summary>Exponential(rate): inverse transform X = -ln(1-U)/rate. Mean 1/rate, variance 1/rate^2, CV^2 = 1.</summary>
public sealed class ExponentialDistribution : DistributionBase
{
    public double Rate { get; }
    public ExponentialDistribution(double rate)
    {
        if (!double.IsFinite(rate) || rate <= 0) throw new ArgumentException("Exponential rate must be positive and finite.", nameof(rate));
        Rate = rate;
    }
    public override string Name => $"Exponential(rate={Rate:G5})";
    public override double Mean => 1 / Rate;
    public override double Variance => 1 / (Rate * Rate);
    public override double Sample(Random rng) => -Math.Log(1.0 - U(rng)) / Rate;
    public override double Cdf(double x) => x <= 0 ? 0 : 1 - Math.Exp(-Rate * x);
}

/// <summary>Deterministic (constant) value, e.g. the "D" of M/D/1. Variance 0.</summary>
public sealed class DeterministicDistribution : DistributionBase
{
    public double Value { get; }
    public DeterministicDistribution(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Deterministic value must be non-negative and finite.", nameof(value));
        Value = value;
    }
    public override string Name => $"Deterministic({Value:G5})";
    public override double Mean => Value;
    public override double Variance => 0;
    public override double Sample(Random rng) => Value;
    public override bool HasCdf => false;       // a point mass has no continuous CDF
    public override double Cdf(double x) => x < Value ? 0 : 1;
}

/// <summary>Uniform(a,b): X = a + (b-a)U. Mean (a+b)/2, variance (b-a)^2/12.</summary>
public sealed class UniformDistribution : DistributionBase
{
    public double A { get; }
    public double B { get; }
    public UniformDistribution(double a, double b)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || a < 0 || b <= a)
            throw new ArgumentException("Uniform needs 0 <= a < b (times cannot be negative).");
        A = a; B = b;
    }
    public override string Name => $"Uniform({A:G5}, {B:G5})";
    public override double Mean => (A + B) / 2;
    public override double Variance => (B - A) * (B - A) / 12;
    public override double Sample(Random rng) => A + (B - A) * U(rng);
    public override double Cdf(double x) => x <= A ? 0 : x >= B ? 1 : (x - A) / (B - A);
}

/// <summary>
/// Normal(mu, sigma) truncated at 0 (negative times are rejected and re-drawn). Variates by Box-Muller.
/// Mean and variance are those of the TRUNCATED distribution, so analytical formulas stay consistent with the sampler.
/// </summary>
public sealed class TruncatedNormalDistribution : DistributionBase
{
    public double Mu { get; }
    public double Sigma { get; }
    private readonly double _alpha, _z, _lambdaHazard, _mean, _variance;

    public TruncatedNormalDistribution(double mu, double sigma)
    {
        if (!double.IsFinite(mu) || mu <= 0) throw new ArgumentException("Normal mean must be positive.", nameof(mu));
        if (!double.IsFinite(sigma) || sigma <= 0) throw new ArgumentException("Normal standard deviation must be positive.", nameof(sigma));
        Mu = mu; Sigma = sigma;
        _alpha = -mu / sigma;
        _z = 1 - Normal.CDF(0, 1, _alpha);                  // P(X >= 0) for the untruncated normal
        _lambdaHazard = Normal.PDF(0, 1, _alpha) / _z;      // phi(alpha)/Z
        _mean = mu + sigma * _lambdaHazard;
        _variance = sigma * sigma * (1 + _alpha * _lambdaHazard - _lambdaHazard * _lambdaHazard);
    }
    public override string Name => $"Normal(mean={Mu:G5}, sd={Sigma:G5}) truncated at 0";
    public override double Mean => _mean;
    public override double Variance => _variance;
    /// <summary>Mean of the untruncated normal minus the truncated mean, relative: how much truncation shifted the mean.</summary>
    public double MeanShiftFraction => Math.Abs(_mean - Mu) / Mu;

    public override double Sample(Random rng)
    {
        while (true)
        {
            // Box-Muller: Z = sqrt(-2 ln U1) cos(2 pi U2)
            double u1 = 1.0 - U(rng), u2 = U(rng);
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            double x = Mu + Sigma * z;
            if (x >= 0) return x;
        }
    }
    public override double Cdf(double x) =>
        x <= 0 ? 0 : (Normal.CDF(Mu, Sigma, x) - (1 - _z)) / _z;
}

/// <summary>
/// Gamma(shape k, scale theta): mean k*theta, variance k*theta^2. Marsaglia-Tsang (2000) squeeze method;
/// shape &lt; 1 uses the boost X = Gamma(k+1) * U^(1/k).
/// </summary>
public sealed class GammaDistribution : DistributionBase
{
    public double Shape { get; }
    public double Scale { get; }
    public GammaDistribution(double shape, double scale)
    {
        if (!double.IsFinite(shape) || shape <= 0 || !double.IsFinite(scale) || scale <= 0)
            throw new ArgumentException("Gamma shape and scale must be positive.");
        Shape = shape; Scale = scale;
    }
    public override string Name => $"Gamma(shape={Shape:G5}, scale={Scale:G5})";
    public override double Mean => Shape * Scale;
    public override double Variance => Shape * Scale * Scale;
    public override double Sample(Random rng)
    {
        double k = Shape;
        double boost = 1.0;
        if (k < 1)
        {
            boost = Math.Pow(1.0 - U(rng), 1.0 / k);
            k += 1;
        }
        double d = k - 1.0 / 3.0, c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            double x, v;
            do
            {
                double u1 = 1.0 - U(rng), u2 = U(rng);
                x = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
                v = 1.0 + c * x;
            } while (v <= 0);
            v = v * v * v;
            double u = 1.0 - U(rng);
            if (u < 1 - 0.0331 * x * x * x * x || Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v)))
                return d * v * Scale * boost;
        }
    }
    public override double Cdf(double x) => x <= 0 ? 0 : Gamma.CDF(Shape, 1 / Scale, x);
}

/// <summary>
/// Erlang-k with a given MEAN: the sum of k independent exponential phases, each with mean mean/k.
/// X = -ln(U1 U2 ... Uk) / (k/mean). Variance mean^2/k, CV^2 = 1/k.
/// </summary>
public sealed class ErlangDistribution : DistributionBase
{
    public int K { get; }
    public double MeanValue { get; }
    public ErlangDistribution(int k, double mean)
    {
        if (k < 1) throw new ArgumentException("Erlang k must be >= 1.", nameof(k));
        if (!double.IsFinite(mean) || mean <= 0) throw new ArgumentException("Erlang mean must be positive.", nameof(mean));
        K = k; MeanValue = mean;
    }
    public override string Name => $"Erlang(k={K}, mean={MeanValue:G5})";
    public override double Mean => MeanValue;
    public override double Variance => MeanValue * MeanValue / K;
    public override double Sample(Random rng)
    {
        double sum = 0;
        for (int i = 0; i < K; i++) sum += -Math.Log(1.0 - U(rng));
        return sum * MeanValue / K;
    }
    public override double Cdf(double x) => x <= 0 ? 0 : Gamma.CDF(K, K / MeanValue, x);
}

/// <summary>Bootstrap sampler: draws uniformly (with replacement) from observed data. Mean/variance are the sample's.</summary>
public sealed class EmpiricalDistribution : DistributionBase
{
    private readonly double[] _values;
    private readonly double _mean, _variance;
    public EmpiricalDistribution(IEnumerable<double> values, string label = "data")
    {
        _values = values.ToArray();
        if (_values.Length == 0) throw new ArgumentException("Empirical distribution needs at least one observation.");
        if (_values.Any(v => !double.IsFinite(v) || v < 0)) throw new ArgumentException("Empirical observations must be finite and non-negative.");
        _mean = _values.Average();
        _variance = _values.Length < 2 ? 0 : _values.Sum(v => (v - _mean) * (v - _mean)) / (_values.Length - 1);
        Label = label;
    }
    public string Label { get; }
    public int Count => _values.Length;
    public override string Name => $"Empirical({Label}, n={_values.Length})";
    public override double Mean => _mean;
    public override double Variance => _variance;
    public override double Sample(Random rng) => _values[rng.Next(_values.Length)];
    public override bool HasCdf => false;
    public override double Cdf(double x) => _values.Count(v => v <= x) / (double)_values.Length;
}

/// <summary>
/// Replays a recorded sequence in order (cyclic if exhausted). This turns the event-driven engine into a
/// trace-driven FCFS replay of real data (arrival gaps / service durations in the original order).
/// </summary>
public sealed class SequenceDistribution : DistributionBase
{
    private readonly double[] _values;
    private int _next;
    private readonly double _mean, _variance;
    public SequenceDistribution(IEnumerable<double> values, string label = "trace")
    {
        _values = values.ToArray();
        if (_values.Length == 0) throw new ArgumentException("Sequence needs at least one value.");
        _mean = _values.Average();
        _variance = _values.Length < 2 ? 0 : _values.Sum(v => (v - _mean) * (v - _mean)) / (_values.Length - 1);
        Label = label;
    }
    public string Label { get; }
    public override string Name => $"Trace replay({Label}, n={_values.Length})";
    public override double Mean => _mean;
    public override double Variance => _variance;
    public override double Sample(Random rng) => _values[_next++ % _values.Length];
    public override bool HasCdf => false;
    public override double Cdf(double x) => _values.Count(v => v <= x) / (double)_values.Length;
    public override void Reset() => _next = 0;
}

public enum DistributionKind { Exponential, Deterministic, Uniform, Normal, Gamma, Erlang, Empirical }

/// <summary>Builds distributions from "mean and coefficient of variation" as typed in the UI.</summary>
public static class DistributionFactory
{
    public static readonly DistributionKind[] AllKinds = Enum.GetValues<DistributionKind>();

    /// <summary>
    /// Creates a distribution with the given mean and CV (= standard deviation / mean).
    /// Exponential ignores cv (CV = 1) and Deterministic has CV = 0.
    /// Uniform needs cv &lt;= 1/sqrt(3) (otherwise a would be negative).
    /// Gamma: shape = 1/cv^2, scale = mean*cv^2.  Erlang: k = round(1/cv^2).  Normal: sd = mean*cv, truncated at 0.
    /// </summary>
    public static IDistribution Create(DistributionKind kind, double mean, double cv, IReadOnlyList<double>? data = null)
    {
        if (kind == DistributionKind.Empirical)
        {
            if (data is null || data.Count == 0) throw new ArgumentException("Empirical distribution needs data (load the CSV first).");
            return new EmpiricalDistribution(data);
        }
        if (!double.IsFinite(mean) || mean <= 0) throw new ArgumentException("Mean must be positive and finite.", nameof(mean));
        if (!double.IsFinite(cv) || cv < 0) throw new ArgumentException("CV must be non-negative and finite.", nameof(cv));

        switch (kind)
        {
            case DistributionKind.Exponential:
                return new ExponentialDistribution(1 / mean);
            case DistributionKind.Deterministic:
                return new DeterministicDistribution(mean);
            case DistributionKind.Uniform:
            {
                double half = Math.Sqrt(3) * cv * mean;
                if (half > mean)
                    throw new ArgumentException($"Uniform with mean {mean:G4} supports CV <= {1 / Math.Sqrt(3):F4} (CV = {cv:G4} would need negative times).");
                if (half <= 0) throw new ArgumentException("Uniform needs CV > 0 (use Deterministic for CV = 0).");
                return new UniformDistribution(mean - half, mean + half);
            }
            case DistributionKind.Normal:
                if (cv <= 0) throw new ArgumentException("Normal needs CV > 0 (use Deterministic for CV = 0).");
                return new TruncatedNormalDistribution(mean, mean * cv);
            case DistributionKind.Gamma:
                if (cv <= 0) throw new ArgumentException("Gamma needs CV > 0 (use Deterministic for CV = 0).");
                return new GammaDistribution(1 / (cv * cv), mean * cv * cv);
            case DistributionKind.Erlang:
                if (cv <= 0) throw new ArgumentException("Erlang needs CV > 0 (use Deterministic for CV = 0).");
                return new ErlangDistribution(Math.Max(1, (int)Math.Round(1 / (cv * cv))), mean);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>Does this kind use the CV input? (Exponential and Deterministic are fixed; Empirical comes from data.)</summary>
    public static bool UsesCv(DistributionKind kind) =>
        kind is DistributionKind.Uniform or DistributionKind.Normal or DistributionKind.Gamma or DistributionKind.Erlang;
}
