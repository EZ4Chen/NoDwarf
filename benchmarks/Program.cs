using NoDwarf.Simulation;

if(args.Contains("--diagnostic-checks")){DiagnosticTraceChecks.Run();return;}

if(args.Contains("--colony-long-run")){ColonyLongRun.Run(args[Array.IndexOf(args,"--colony-long-run")+1]);return;}

if (args.Contains("--realtime-simulation")) { RealtimeSimulationBenchmark.Run(args.Contains("--profile")); return; }

if(args.Contains("--equipment-dimensions-checks")){EquipmentDimensionsChecks.Run();return;}
if(args.Contains("--melee-checks")){MeleeChecks.Run();return;}
if(args.Contains("--combat-checks")){CombatChecks.Run();return;}

OptimizationChecks.Run();
NavigationChecks.Run();
DemoChecks.Run();
Console.WriteLine("NAVIGATION_CHECKS_OK");
if (!args.Contains("--checks-only")) NavigationBenchmarks.Run();
