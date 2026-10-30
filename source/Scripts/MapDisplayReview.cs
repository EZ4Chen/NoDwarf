using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CheckMapDisplay()
    {
        try
        {
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            _clock.Paused=true;_world=new DemoWorld(region:"temperate");_world.DeveloperResidents(false);_overview.World=_world;_overview.RefreshMap();
            var tree=_world.Trees.Keys.First();int draws=_overview.MapRefreshCount;
            Check(_overview.MapPixel(tree.X,tree.Z)==new Color("35472f"),"initial tree shown");
            Check(_world.DamageBlock(tree,double.MaxValue),"fell actual tree");
            for(int i=0;i<30;i++)_overview.RefreshMap();
            _overview.AdvanceMapRefresh(4.9);
            Check(_overview.MapRefreshCount==draws&&_overview.MapPixel(tree.X,tree.Z)==new Color("35472f"),"edits coalesced before five seconds");
            _overview.AdvanceMapRefresh(.2);
            Check(_overview.MapRefreshCount==draws+1&&_overview.MapPixel(tree.X,tree.Z)!=new Color("35472f"),"felled tree refreshed while simulation paused");
            _overview.AdvanceMapRefresh(10);Check(_overview.MapRefreshCount==draws+1,"unchanged map does not upload texture");
            _world.Terrain.SetTree(tree,true);_overview.RefreshMap();_overview.AdvanceMapRefresh(10,false);
            Check(_overview.MapRefreshCount==draws+1,"hidden map defers upload");
            _overview.AdvanceMapRefresh(0);Check(_overview.MapPixel(tree.X,tree.Z)==new Color("35472f"),"opening hidden map catches up, including tree growth");
            _world.Terrain.Excavate(tree+new Cell(0,0,-1));_overview.AdvanceMapRefresh(5);
            Check(_overview.MapPixel(tree.X,tree.Z)==new Color("514d46"),"surface excavation refreshed");
            _world=new DemoWorld(flat:true);_overview.World=_world;_overview.RefreshMap();
            Check(_overview.MapPixel(tree.X,tree.Z)!=new Color("514d46"),"new world replaces map immediately");

            var atlas=new WorldAtlas{Size=new(720,500)};AddChild(atlas);var settings=new WorldGenerationSettings{Seed=23519}.WithRecommendedSite();atlas.SetWorld(settings);
            int builds=atlas.TextureBuildCount;
            atlas.SetWorld(settings with {SiteX=0,SiteY=0});Check(atlas.TextureBuildCount==builds,"selection does not regenerate atlas");
            int selectedX=-1,selectedY=-1;atlas.Selected=(x,y)=>{selectedX=x;selectedY=y;};
            var rect=atlas.MapBounds;
            atlas._GuiInput(new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left,Position=rect.End-new Vector2(1,1)});
            Check(selectedX==settings.Width-1&&selectedY==settings.Height-1,"bottom right maps to last region");
            selectedX=-1;atlas._GuiInput(new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left,Position=new Vector2(1,1)});
            Check(selectedX==-1,"letterbox click ignored");
            atlas._GuiInput(new InputEventKey{Pressed=true,Keycode=Key.Right});Check(selectedX==1&&selectedY==0,"keyboard region selection preserved");
            atlas.Layer=AtlasLayer.Elevation;Check(atlas.TextureBuildCount==builds+1&&atlas.AccessibilityDescription.Contains("等高"),"layer rebuild and accessible legend");
            atlas.SetWorld(settings with {Temperature=2});Check(atlas.TextureBuildCount==builds+2,"parameter change invalidates cached atlas");
            foreach(int dimension in new[]{65,129,257})
            {
                var modern=(settings with {Revision=2,WorldSize=dimension}).WithRecommendedSite();atlas.SetWorld(modern);
                rect=atlas.MapBounds;atlas._GuiInput(new InputEventMouseButton{Pressed=true,ButtonIndex=MouseButton.Left,Position=rect.End-new Vector2(.01f,.01f)});
                Check(selectedX==dimension-1&&selectedY==dimension-1,"modern map size maps bottom-right accurately");
            }
            atlas.QueueFree();

            var layer=new CanvasLayer{Layer=100};AddChild(layer);
            var panel=new PanelContainer();panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);layer.AddChild(panel);
            var grid=new GridContainer{Columns=2};panel.AddChild(grid);
            foreach(var mode in new[]{AtlasLayer.Biome,AtlasLayer.Elevation,AtlasLayer.Rainfall,AtlasLayer.Water})
            {
                var column=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill};grid.AddChild(column);
                column.AddChild(new Label{Text=mode switch{AtlasLayer.Biome=>"生态与海岸",AtlasLayer.Elevation=>"海拔与等高分区",AtlasLayer.Rainfall=>"降水",_=>"水系与流向"}});
                var map=new WorldAtlas{CustomMinimumSize=new(440,250),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill};column.AddChild(map);map.SetWorld((settings with {Revision=2,WorldSize=129}).WithRecommendedSite());map.Layer=mode;
                column.AddChild(new Label{Text=map.Legend,AutowrapMode=TextServer.AutowrapMode.WordSmart});
            }
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(DisplayServer.GetName()!="headless")
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng("res://.local/atlas-readability.png");}
            GD.Print("MAP_DISPLAY_OK: five-second batching, real felling, growth, paused/hidden updates, excavation, world replacement, no redundant uploads, atlas layers/cache, coast/contours, keyboard and mouse selection");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
