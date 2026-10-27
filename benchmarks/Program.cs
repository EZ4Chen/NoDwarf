using NoDwarf.Simulation;

if(args.Contains("--colony-long-run")){ColonyLongRun.Run(args[Array.IndexOf(args,"--colony-long-run")+1]);return;}

if (args.Contains("--realtime-simulation")) { RealtimeSimulationBenchmark.Run(args.Contains("--profile")); return; }

OptimizationChecks.Run();
NavigationChecks.Run();
DemoChecks.Run();
Console.WriteLine("NAVIGATION_CHECKS_OK");
if (!args.Contains("--checks-only")) NavigationBenchmarks.Run();
