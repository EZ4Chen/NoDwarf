using Godot;
using NoDwarf.Simulation;
using System.Text.Json;

namespace NoDwarf;
public partial class Main
{
    private sealed class HeldSave
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release=new();
        public int Value {get{Started.TrySetResult();if(!Release.Wait(TimeSpan.FromSeconds(30)))throw new TimeoutException("Test write barrier");return 1;}}
    }
    private async void ReviewSession()
    {
        HeldSave? held=null;
        string directory=ProjectSettings.GlobalizePath("res://.local/session-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            using(var reference=Image.CreateEmpty(1,1,false,Image.Format.Rgba8))
            {
                var bytes=new byte[4];
                foreach(var color in new[]{new Color(.5f,.27f,.99f,1),new Color(-.1f,1.2f,.3f,.6f),new Color("35472f"),new Color("514d46")})
                {
                    reference.SetPixel(0,0,color);WorldOverview.EncodeMapPixel(color,bytes);
                    Check(reference.GetData().SequenceEqual(bytes),"Bulk minimap encoding differs from Image.SetPixel");
                }
            }
            var transforms=new[]{Transform3D.Identity,new Transform3D(new Basis(new Vector3(0,0,1),new(-1,0,0),new(0,-1,0)),new Vector3(2,3,4))};
            var packed=TerrainView.PackTreeTransforms(transforms);
            for(int i=0;i<transforms.Length;i++)
            {
                int p=i*12;var decoded=new Transform3D(new Basis(new Vector3(packed[p],packed[p+4],packed[p+8]),new(packed[p+1],packed[p+5],packed[p+9]),new(packed[p+2],packed[p+6],packed[p+10])),new(packed[p+3],packed[p+7],packed[p+11]));
                Check(decoded==transforms[i],"Packed forest matrix layout changed geometry");
            }
            _clock.Paused=true;_creative=false;Directory.CreateDirectory(directory);_activeSavePath=Path.Combine(directory,"save.json");
            await SaveService.Ready;
            _world.DeveloperResidents(true);CreatePeople();
            var owner=_world;long xp=owner.Workers[0].Person.GrowthXp;
            held=new HeldSave();var barrier=_saveService.Write(Path.Combine(directory,"barrier.json"),held);
            await held.Started.Task;
            var first=SaveSessionAsync();
            owner.Workers[0].Person.GrowthXp=xp+100;
            Check(ReferenceEquals(first,SaveSessionAsync()),"Repeated saves must share one bounded request loop");
            for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Check(!first.IsCompleted&&!File.Exists(SavePath),"Save acknowledged before disk commit");
            held.Release.Set();await barrier;Check(await first,"Save failed");
            using(var json=JsonDocument.Parse(File.ReadAllText(SavePath)))
                Check(!json.RootElement.TryGetProperty("World",out _)&&json.RootElement.GetProperty("WorldState").ValueKind==JsonValueKind.Object,"New session double-encoded world JSON");
            var prepared=await Task.Run(()=>PrepareSession(SavePath));
            Check(prepared.Forest.Roots.Length==prepared.World.Trees.Count&&prepared.Forest.Batches.All(b=>b.Buffer.Length==b.Heights.Length*12&&b.Heights.SequenceEqual(b.Heights.Order())),"Prepared forest lost roots or height ordering");
            Check(prepared.State.TickRate==60&&prepared.Clock.Tick==_clock.Tick,"New session clock rate/roundtrip");
            var oldClock=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(SavePath))!;
            oldClock["TickRate"]=30;
            string oldClockPath=Path.Combine(directory,"old-clock.json");File.WriteAllText(oldClockPath,oldClock.ToJsonString());
            bool rejectedRate=false;try{PrepareSession(oldClockPath);}catch(ArgumentException){rejectedRate=true;}
            Check(rejectedRate,"Old clock rate accepted");
            Check(prepared.World.Workers[0].Person.GrowthXp==xp+100,"Coalesced save lost latest requested state");
            owner.Workers[0].Person.GrowthXp=xp+200;
            await LoadSessionAsync();Check(_world!=owner&&_world.Workers[0].Person.GrowthXp==xp+100,"Background load did not publish prepared state");
            GD.Print($"SESSION_LOAD_MEASURE: residents={_world.Workers.Count} background_prepare_ms={_sessionPrepareMs:F3} main_publish_ms={_sessionPublishMs:F3}");
            int forestFrames=0;
            while(_terrainView.PendingForestUploads>0&&forestFrames++<300)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Check(_terrainView.PendingForestUploads==0&&_terrainView.ForestUploadFrames>1,"Prepared forest must finish over bounded frame uploads");
            GD.Print($"FOREST_UPLOAD_MEASURE: frames={_terrainView.ForestUploadFrames} max_ms={_terrainView.MaxForestUploadMs:F3}");

            // Reject old outer sessions without publishing a replacement world.
            var currentWorld=_world;prepared.State.Version=1;
            File.WriteAllText(SavePath,JsonSerializer.Serialize(prepared.State,DemoWorld.SaveOptions));
            await LoadSessionAsync();Check(_world==currentWorld,"Old session replaced current world");
            var retained=_world;File.WriteAllText(SavePath,"broken");await LoadSessionAsync();Check(_world==retained,"Bad load replaced current world");

            // A world switch during preparation must discard the completed old load.
            Check(await SaveSessionAsync(),"Save for stale-load check");
            var loading=LoadSessionAsync();var replacement=DemoWorld.LoadJson(_world.SaveJson());_world=replacement;
            await loading;Check(_world==replacement,"Stale load overwrote a newer world");
            _overview.World=_world;CreatePeople();

            // Real close dialog must stay in this scene after a commit failure.
            string blocked=Path.Combine(directory,"blocked");Directory.CreateDirectory(blocked);_activeSavePath=blocked;_clock.Paused=false;
            RequestWindowQuit();_titleDialog!.Hide();_titleDialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            Check(_sessionLeaving,"Save-and-exit did not gate further world changes");
            if(_saveTask is {} failed)await failed;
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Check(!_sessionLeaving&&!_clock.Paused&&GetTree().CurrentScene==this,"Failed save exited or left the game frozen");
            _clock.Paused=true;_activeSavePath=Path.Combine(directory,"save.json");

            // A successful close must wait for the queued disk operation.
            held=new HeldSave();barrier=_saveService.Write(Path.Combine(directory,"barrier.json"),held);await held.Started.Task;
            RequestWindowQuit();_titleDialog!.Hide();_titleDialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            Check(_saveTask is {IsCompleted:false}&&GetTree().CurrentScene==this,"Save-and-exit skipped pending disk work");
            held.Release.Set();await barrier;if(_saveTask is {} saved)Check(await saved,"Final save failed");
            Check(PrepareSession(SavePath).World.Workers[0].Person.GrowthXp==xp+100,"Final committed save lost state");
            GD.Print("SESSION_ASYNC_OK: frame progress during write, coalescing, current format and rejected old formats, failed/stale load, failed exit and committed exit");
            Directory.Delete(directory,true);GetTree().Quit();
        }
        catch(Exception e){held?.Release.Set();GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
