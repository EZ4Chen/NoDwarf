using NoDwarf.Simulation;
if(args.Contains("--plant-updates-test")){PlantUpdateChecks.Run();CropClimateChecks.Run();FarmingChecks.Run();CropProductionChecks.Run();TreeShapeChecks.Run();BuildingGravityChecks.Run();FluidChecks.Run();return;}
if(args.Contains("--tick-rate-regression")){TickRateChecks.Run();CalendarChecks.Run();ResidentChecks.Run();ResidentHealthChecks.Run();HealthConditionChecks.Run();ResidentLivingChecks.Run();MedicalRescueChecks.Run();CombatChecks.Run();FluidChecks.Run();return;}
if(args.Contains("--creature-resident-regression")){CreatureChecks.Run();ResidentChecks.Run();ResidentHealthChecks.Run();HealthConditionChecks.Run();ResidentLivingChecks.Run();MedicalRescueChecks.Run();CombatChecks.Run();MeleeChecks.Run();return;}
if(args.Contains("--creature-framework-test")){CreatureChecks.Run();return;}

if(args.Contains("--water-regression")){NavigationChecks.Run();Console.WriteLine("WATER_NAVIGATION_REGRESSION_OK");WorldGenerationChecks.Run();ResidentLivingChecks.Run();MedicalRescueChecks.Run();LifeExpansionChecks.Run();return;}
if(args.Contains("--water-safety-checks")){WaterSafetyChecks.Run();FluidChecks.Run();return;}
if(args.Contains("--world-generation-checks")){WorldGenerationChecks.Run();return;}

if(args.Contains("--hospital-checks")){LifeHospitalChecks.Run();return;}
if(args.Contains("--life-expansion-checks")){LifeExpansionChecks.Run();return;}
if(args.Contains("--living-regression")){ResidentChecks.Run();ResidentIntegrationChecks.Run();ResidentPlanningChecks.Run();CombatChecks.Run();return;}
if(args.Contains("--rescue-checks")){MedicalRescueChecks.Run();return;}
if(args.Contains("--health-checks")){ResidentHealthChecks.Run();HealthConditionChecks.Run();return;}
if(args.Contains("--living-checks")){ResidentLivingChecks.Run();return;}
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
