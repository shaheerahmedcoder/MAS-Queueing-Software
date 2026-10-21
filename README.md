# QUEUE LAB – Queueing Model Simulator (C# / .NET 8 / WinForms)

A desktop dashboard for **M/M/1, M/M/c, M/G/1 and G/G/1** queues. For every model it shows
**(a) the exact or approximate formulas with your numbers substituted** and **(b) a real discrete-event simulation**,
side by side, with animated queue layout, charts and chi-square validation. It also fits the KFC measurements and
replays them through the same models.

*Course: Modeling & Simulation / Queueing Theory.*  Windows only (WinForms).

---

## 1. Quick start

Requirements: Windows 10/11 and the **.NET 8 SDK** (a newer SDK also works – the projects target `net8.0`).
Free NuGet packages only: OxyPlot.WindowsForms (charts), MathNet.Numerics (chi-square / t quantiles), xUnit (tests).

```bat
:: open a terminal in the QueueingSuite folder
dotnet run --project QueueingSuite.App          :: start the dashboard  (or double-click run-app.bat)
dotnet build QueueingSuite.sln                  :: 0 warnings, 0 errors
dotnet test                                     :: 137 tests, all green
```

In VS Code: open the folder, `Terminal ▸ Run Task`, or just use the commands above. In Visual Studio: open `QueueingSuite.sln`, set
`QueueingSuite.App` as start-up project.

Headless UI smoke test (writes a PNG for each scenario, exits non-zero if a scenario throws):
```bat
dotnet run --project QueueingSuite.App -- --snapshot out
```

## 2. Solution layout

```
QueueingSuite.sln
  QueueingSuite.Core/        class library, net8.0, NO WinForms dependency  (all math lives here)
    Analytical/   MM1Calculator, MMcCalculator (Erlang C), MG1Calculator (Pollaczek-Khinchine), GG1Calculator (Kingman, Allen-Cunneen)
    Simulation/   Distributions, SimulationEngine (event list, c servers, FCFS), ReplicationRunner, SimulationReport (workings, CSV)
    Statistics/   ChiSquareTest, SimulationValidator, ConfidenceInterval, Descriptive
    Data/         CsvDataLoader (both formats), DataSessions (fit), TraceReplay, KfcComparison
    Models/       QueueInputs, AnalyticalResult, SimulationResult, ComparisonResult, Fmt
    QueueRunner.cs, InputValidator.cs   what the UI calls: typed inputs -> distributions, formulas, simulation, comparison
  QueueingSuite.App/         WinForms dashboard (net8.0-windows): MainForm + Controls/*  (no queueing math)
  QueueingSuite.Tests/       xUnit, 137 tests
  docs/   AUDIT.md   FORMULAS.md   TEST_REPORT.md
```
The two original folders (`current ui`, `all models`) are untouched. Their ideas live on: the dashboard look-and-feel in `Controls/*`, the trace replay in `Data/TraceReplay.cs`.

## 3. Using the dashboard

### Sidebar (dark navy)
* **Mode** – `Analytical` (formulas only; results update as you type), `Simulation` (discrete-event run), `Compare` (both, with % error).
* **Model** and the fields that model needs (other fields are hidden, nothing is "dead"):

  | Model | Fields |
  |---|---|
  | M/M/1 | λ, μ |
  | M/M/c | λ, μ, number of servers c (1–50) |
  | M/G/1 | λ, μ (mean service = 1/μ), service distribution, service CV (σ/mean) |
  | G/G/1 | arrival distribution + CV, λ, service distribution + CV, μ |

  Distributions: Exponential, Deterministic, Uniform, Normal (truncated at 0), Gamma, Erlang-k, Empirical (from the loaded data).
  The grey text under the fields shows E[S], Var(S), Cs², Ca² and ρ for what you typed (turns amber if ρ ≥ 1).
* **Customers** (default 10 000, max 2 000 000), **warm-up** customers (excluded from statistics), **seed** (blank = random – the seed used is always shown),
  **replications** (> 1 gives mean ± 95 % confidence interval).
* **Use KFC data** – λ, μ (and Ca², Cs² for G/G/1) are fitted from the chosen session, and *Simulation* becomes a trace replay of the recorded customers.
* **Run** (Enter also runs) and **Cancel**. Runs happen on a worker thread with a progress bar; the window never freezes.
  Errors are shown in red with a specific message (e.g. `Arrival rate λ: 'abc' is not a number`); ρ ≥ 1 is a warning, not a crash.

### Top: the six metric cards
Utilization (%), L, Lq, W, Wq, Customers served – plus a strip with ρ, P0, P(wait), the seed and a badge:
**EXACT** (M/M/1, M/M/c, M/G/1), **APPROXIMATION (Kingman)** (G/G/1) or **UNSTABLE**.
Analytical mode shows the formula value; Simulation mode shows the simulated value (± CI); Compare mode shows the simulated value with the formula and % error underneath.

### Tabs
1. **Results & formulas** – table (Measure | Analytical | Simulated | % error; green ≤ 3 %, amber ≤ 10 %) and the **Formula workings** panel:
   every formula with your numbers, e.g. `ρ = λ/μ = 4/6 = 0.6667`, `Lq = ρ²/(1−ρ) = 0.4444/0.3333 = 1.3333`, followed by how each simulated number was measured.
2. **Queue animation** – entrance → FIFO waiting line (capped, "+N more") → c server boxes → exit. Idle servers are green, busy ones amber (red when the line is long)
   and show the customer number. Play / Pause / Step / Restart and a speed slider replay the simulation's own event log. The log is capped at 30 000 events (≈ 10 000 customers).
3. **Charts** – (a) Lq vs ρ with the operating point (and the simulated point), (b) running mean of Wq vs customer number with the formula as a dashed line,
   (c) number in queue over time (step plot), (d) waiting-time histogram, (e) per-server utilization (M/M/c).
4. **Chi-square tests** – see §6.
5. **Customers** – the first N customers (N adjustable) with arrival, start, wait, service, end, time in system and server;
   **Export CSV…** writes the inputs, the analytical-vs-simulated table (as `#` comment lines) and the **full** customer trace.
6. **KFC data** – loaded file, fitted λ, μ, ρ, Ca², Cs² for Session 1 / Session 2 / All, and every model side by side with the trace replay
   (unstable sessions are marked in red). `Open CSV…` loads either file format; `Use in dashboard` sends a session to the sidebar.

## 4. The models – assumptions and how to read the results

| Model | Result | Assumptions |
|---|---|---|
| M/M/1 | **exact** | Poisson arrivals (rate λ), exponential service (rate μ), 1 server, FCFS, infinite queue |
| M/M/c | **exact** (Erlang C) | as above with c identical servers sharing one queue; ρ = λ/(cμ) |
| M/G/1 | **exact** (Pollaczek-Khinchine) | Poisson arrivals, any service distribution (only mean and variance matter), 1 server |
| G/G/1 | **approximation** (Kingman) | any arrival and service distribution (only means and variances matter), 1 server – accuracy is best near ρ = 1 and poorer at light load |

* **ρ ≥ 1** means arrivals outpace service: no steady state exists. The formulas are then reported as *unstable* (cards say "unstable", never ∞/NaN) while the simulation still runs
  – its averages keep growing with the run length, so do not treat them as steady-state values.
* **P0** = probability the system is empty. **P(wait)** = probability an arriving customer waits (Erlang C for M/M/c; ρ for M/M/1 and M/G/1; not defined for G/G/1).
* **Time units** are whatever you type; the KFC data are in minutes.
* Simulation vs formula: a run of 10 000 customers is noisy (a few %, more for Lq/Wq and for ρ near 1). Increase customers or use replications and watch the CI shrink.
  200 000 customers × 10 replications agree with the formulas to < 2 % (`docs/TEST_REPORT.md`).

### Simulation details
Discrete-event engine: future-event list (arrival, departure), c servers, one FCFS line, lowest-numbered idle server first.
L and Lq are **time-weighted** integrals, utilization is busy time ÷ (c·T), warm-up starts the observation window at the arrival of customer (warm-up + 1).
With M/M/c and light load the first servers are busier than the last – that is the dispatch rule, not an error. Full formulas: `docs/FORMULAS.md`.

## 5. KFC data – the two CSV files and the decision taken
`QueueingSuite.App/Data/kfc_tandem_data.csv` (columns `CustomerID, ArrivalMins, Q1StartMins, Q1EndMins, Q2EndMins, WaitQ1, ServiceQ1, ServiceQ2`) is the **source of truth** and is loaded
at start-up. The dashboard's old `kfc_data.csv` (`ArrivalMinutes, ObservedWaitMinutes, ServiceMinutes, Queue2Minutes`) is *not* a Queue-2 view of it: it is a differently timed,
internally consistent record of mostly the same 80 customers (details and numbers: `docs/AUDIT.md`). Both files can be opened (`Open CSV…`); malformed rows are rejected with the line number.

Queue 1 is the single-server queue the models are fitted to: λ = 1/mean arrival gap, μ = 1/mean service.
**Session 1** (arrival ≤ 90 min, 20 customers, ρ = 0.53) is stable; **Session 2** (60 customers, ρ = 1.095) is **unstable**;
**All data** mixes both regimes (and a 25-minute gap, which inflates Ca²), so interpret it with care.
The replay of the recorded rows gives Wq = 0.60 min for Session 1 while the recorded `WaitQ1` column averages 1.60 min: the file lists only a sample of the customers
(80 of 96 IDs), the missing customers also used the server. Both numbers are shown on the KFC tab.

## 6. Chi-square validation (how to read it)
Pick the test, α (default 0.05), then **Run test**. You get a verdict (green = *fail to reject H0*, red = *reject H0*, amber = test not possible),
χ², df = bins − 1 − (estimated parameters), the critical value, the p-value, the observed / expected / (O−E)²/E table and a bar chart.
Reject H0 when χ² > critical value (p < α). "Fail to reject" means *no evidence against* the distribution, not proof.

1. **Generator validity – inter-arrival times** and 2. **– service times**: are the simulation's random numbers really from the intended distribution (Exponential, Uniform, Normal, Gamma, Erlang)? Needs a simulation run.
3. **Data fit – KFC inter-arrival times** and 4. **– service times** ~ Exponential: justifies (or not) M/M/1 for the data. Honest result: it **rejects** for Session 2 and All data
   (service times are almost constant), and Session 1 has too few points for a powerful test.
5. **Simulation vs theory P(n)** (M/M/1, M/M/c): compares the simulated time-in-state distribution with the analytical P(n). Because queue states are autocorrelated the test uses an *effective*
   sample size (customers ÷ spacing); *auto* spacing is calibrated so a correct model is rejected only ≈ 5 % of the time (`docs/TEST_REPORT.md` §D).

## 7. Known limitations / open points
* Windows + .NET 8 only (WinForms). No PDF report (optional stretch not done); results export to CSV instead.
* M/G/c and G/G/c are not offered (Allen-Cunneen for G/G/c exists in code but is not wired to the UI).
* Normal service/arrival times are truncated at 0; for large CV the actual mean differs from the typed one (a warning says so and the formulas use the actual moments).
* The event log (animation) is capped at 30 000 events; charts and tables use the whole run.
* Maximum 2 000 000 customers per run and 50 000 000 in total with replications (memory).
* Kingman is only an approximation (see §4). The chi-square P(n) test is conservative for ρ ≥ 0.9 with few customers (needs more data).
* Single-run noise is real: use replications for tight agreement (see the test report).

## 8. Documents
* `docs/AUDIT.md` – what was wrong in the originals and how the CSV files relate.
* `docs/FORMULAS.md` – every formula, every algorithm, and the file/method where it lives.
* `docs/TEST_REPORT.md` – the tests, inputs, expected vs actual values, calibration experiments.
