using Godot;
namespace NoDwarf;

// Local vector artwork: no font glyph dependency, external download or texture-pack coupling.
public static class UiIcons
{
    private static readonly Dictionary<string,Texture2D> Cache=new();
    public static Texture2D Get(string name)
    {
        if(Cache.TryGetValue(name,out var texture))return texture;
        string shape=name.ToLowerInvariant() switch
        {
            "select" => "<path fill='#e4e9dd' stroke-width='2.5' d='M9 4v35l9-9 7 14 7-4-8-13 14-1Z'/>",
            "wall" => "<path fill='#939f99' d='M5 7h38v34H5Z'/><path stroke='#303a34' d='M5 18h38M5 30h38M24 7v11M15 18v12m18-12v12M24 30v11'/><path stroke='#c1c9bb' d='M8 10h32M8 33h12'/>",
            "mill" => "<ellipse fill='#8b989a' cx='24' cy='32' rx='20' ry='11'/><ellipse fill='#b3bcb5' cx='24' cy='24' rx='20' ry='11'/><path fill='#5b665f' d='M20 21h8v8h-8Z'/><path stroke='#b38b58' stroke-width='5' d='M24 23V9h15V4'/>",
            "loom" => "<path fill='#ab8255' d='M5 4h7v40H5Zm31 0h7v40h-7ZM12 8h24v5H12Zm0 27h24v5H12Z'/><path fill='#d5cfb8' d='M15 14h18v20H15Z'/><path stroke='#988b73' d='M19 14v20m5-20v20m5-20v20M15 20h18M15 26h18'/>",
            "workbench" => "<path fill='#b18b5d' d='M4 19h40v8H4Zm5 8h6v16H9Zm24 0h6v16h-6Z'/><path stroke='#bfa574' stroke-width='4' d='m15 17 10-9'/><path fill='#a9b9be' d='m20 6 5-4 10 9-5 5Z'/><path fill='#667c86' d='M31 16h9v12h-9Z'/>",
            "mark_chop" => "<rect x='3' y='3' width='42' height='42' rx='8' fill='#263039' stroke='#edc47b' stroke-width='3'/><path stroke='#edc47b' stroke-width='5' d='m15 36 15-25'/><path fill='#fff0c5' d='m27 10 12 6-7 12-11-11Z'/>",
            "mark_dig" => "<rect x='3' y='3' width='42' height='42' rx='8' fill='#263039' stroke='#edc47b' stroke-width='3'/><path stroke='#edc47b' stroke-width='5' d='m14 36 16-23'/><path fill='#fff0c5' d='M9 14q17-10 30 12L28 18 18 18Z'/>",
            "ladder" => "<path stroke='#c29c66' stroke-width='5' d='M12 4v40M36 4v40M12 10h24M12 20h24M12 30h24M12 40h24'/>",
            "floodgate" => "<path fill='#829ba4' d='M5 4h6v32H5zm32 0h6v32h-6ZM12 8h24v21H12Z'/><path stroke='#6cbbe1' stroke-width='3' d='M3 35q5-5 10 0t10 0t10 0t12 0M3 43q5-5 10 0t10 0t10 0t12 0'/>",
            "growingzone" => "<path fill='#6b9a68' d='M6 20h24V9h12v33H6Z'/><path stroke='#d9e8b9' stroke-width='3' d='M23 33V15'/><path fill='#b1d18b' d='M23 24Q7 24 9 10q14 1 14 14Zm0-7Q24 3 37 5q0 13-14 12Z'/>",
            "stockpile" => "<path fill='#ab82ad' d='M5 7h25v13h13v22H17V30H5Z'/><path stroke='#e4c6e6' d='M12 14h12m0 13h12m-12 8h12'/>",
            "unstockpile" or "unfarm" => "<path fill='#7d8f84' d='M6 7h24v13h12v22H17V30H6Z'/><path stroke='#da8074' stroke-width='7' d='m6 42 36-36'/>",
            "sapling" or "birch_sapling" or "pine_sapling" or "acacia_sapling" or "jungle_sapling" or "planttree" => "<path stroke='#a07d50' stroke-width='5' d='M24 42V18'/><path fill='#77a362' d='M24 27Q3 29 7 8q18 1 17 19Zm0-6Q22 3 42 6q3 19-18 15Z'/>",
            "chop" => "<path stroke='#b78f61' stroke-width='6' d='m13 42 18-33'/><path fill='#b0c2c9' d='m26 8 17 7-8 15-14-13Z'/>",
            "table" or "gametable" => "<path fill='#b99a69' d='M5 16h38v8H5ZM9 24h5v19H9Zm25 0h5v19h-5Z'/><path fill='#e8d6a5' d='M14 9h9v7h-9Zm9-7h9v7h-9Zm9 7h9v7h-9Z'/>",
            "chair" => "<path fill='#b99a69' d='M10 4h28v24H10Zm-3 25h34v6H7Zm3 5h5v10h-5Zm23 0h5v10h-5Z'/>",
            "water_bucket" or "bucket" => "<path fill='#ad8f65' d='m8 16 5 27h22l5-27Z'/><path fill='#77c5dc' d='M8 16q16 8 32 0-16-8-32 0Z'/><path fill='none' stroke='#d7c69c' stroke-width='3' d='M10 17V9q14-13 28 0v8'/>",
            "splint" or "prosthesis" => "<path fill='#c7a26f' d='m12 4 8 2-4 36-8-2Zm20 0 8 2-4 36-8-2Z'/><path stroke='#e2decf' stroke-width='7' d='M10 14h29M8 31h29'/>",
            "bed" => "<path fill='#bd9a6a' d='M6 15h36v27H6Z'/><path fill='#994c48' d='M10 26h28v10H10Z'/><path fill='#ede5d0' d='M10 18h28v8H10Z'/>",
            "campfire" => "<path stroke='#ab7b4c' stroke-width='6' d='m8 39 31-7M9 32l30 8'/><path fill='#eaa24f' d='M16 30Q4 20 24 3q-2 13 6 8 19 17 2 21Z'/>",
            "cotton" or "cotton_seed" => "<path stroke='#8f9a63' d='M24 42V20'/><g fill='#eee7d7'><circle cx='15' cy='22' r='10'/><circle cx='24' cy='13' r='10'/><circle cx='34' cy='23' r='10'/></g>",
            "potato" or "potato_seed" => "<path fill='#b99869' d='M8 15Q22 1 37 13q16 17-3 28Q5 49 8 15Z'/><path d='m15 21 3 1m12-8 2 2m-5 15 3 1'/>",
            "cloth" or "flour" => "<path fill='#e5d9bd' d='M10 7h30l-4 34H6Z'/><path d='M13 15h21m-23 9h22m-24 9h22'/>",
            "stone" or "cobblestone" => "<path fill='#9fa9ae' d='M7 23 14 10 31 6 44 17 41 37 22 43 6 35Z'/><path d='m14 10 9 13 21-6M23 23l-1 20M7 23l16 0'/>",
            "log" or "wood" => "<path fill='#a67b51' d='m9 16 23-8 12 21-24 12Z'/><ellipse fill='#d3b383' cx='14' cy='29' rx='8' ry='13' transform='rotate(-28 14 29)'/><path d='m25 17 13-4m-9 13 13-4'/>",
            "planks" => "<path fill='#b78f60' d='M6 8h36v10H6zm0 12h36v10H6zm0 12h36v10H6z'/><path d='M14 11v4m20 8v4M14 35v4'/>",
            "stonebricks" => "<path fill='#a2a6a2' d='M5 7h38v34H5Z'/><path d='M5 18h38M5 30h38M24 7v11M15 18v12m18-12v12M24 30v11'/>",
            "grain_seed" or "wheat" => "<path fill='#d8b66c' d='M9 32q-7-17 7-21 10 8 3 21Zm17 5q-4-18 10-20 9 11 0 21Z'/><path d='m14 16 1 13m17-6-1 10'/>",
            "mushroom_spawn" or "mushroom" => "<path fill='#dacbb9' d='M20 24h8l4 19H16Z'/><path fill='#b993c7' d='M5 27Q7 3 24 5q17-2 19 22Z'/><circle fill='#eeddec' cx='17' cy='18' r='3'/><circle fill='#eeddec' cx='31' cy='15' r='3'/>",
            "raw_food" => "<path fill='#91b66b' d='M25 40C3 27 5 9 20 8c2 11 8 16 5 32Z'/><path fill='#cda366' d='M28 40c-2-15 5-25 13-25 4 12-2 21-13 25Z'/>",
            "meal" => "<path fill='#d8b777' d='M6 27c0-21 36-21 36 0v10H6Z'/><path d='m16 13-3 9m13-11-3 11m13-8-3 9'/>",
            "shirt" => "<path fill='#a9b69b' d='m15 8 9 4 9-4 11 10-7 9-5-4v19H16V23l-5 4-7-9Z'/><path d='M19 10q5 9 10 0M17 35h14'/>",
            "trousers" => "<path fill='#8a9294' d='M13 5h22l3 37H26l-2-23-2 23H10Z'/><path d='M13 12h22M24 6v10'/>",
            "boots" => "<path fill='#a1805b' d='M10 7h12v25l-3 10H4V31l6-4Zm19 0h12v20l4 6v9H27V32l2-5Z'/><path d='M4 37h15m8 0h18M11 15h10m9 0h10'/>",
            "gloves" => "<path fill='#c3b393' d='M7 14h7V7h7v22l-4 13H6L3 24l4-3Zm24-7h7v7h7v7l3 3-3 18H34l-4-13Z'/>",
            "cap" => "<path fill='#ac9063' d='M7 28V20C7 3 39 3 39 20v8Z'/><path fill='#806746' d='M5 27h38v8H5Z'/><path d='M24 7v19'/>",
            "hammer" => "<path stroke='#b8905c' stroke-width='7' d='m12 41 20-26'/><path fill='#acbdc3' d='m19 7 8-5 18 16-8 9Z'/>",
            "shield" => "<path fill='#b49a75' d='m7 8 17-4 17 4v19L24 45 7 27Z'/><path d='M17 6v29m14-29v29M8 17h32'/>",
            "bandage" => "<path fill='#e4ddc9' d='m6 17 11-11 25 25-11 11Z'/><path fill='#bcb19b' d='m16 18 3-3 14 14-4 4Z'/><path d='m11 15 4-4m18 26 4-4'/>",
            "raw_iron" => "<path fill='#bc9479' d='M8 29 13 12 29 7 41 22 33 40 15 41Z'/><path d='m13 12 10 14 18-4M23 26l-8 15'/>",
            "deep_rubble" => "<path fill='#676b7a' d='m6 28 12-17 20 3 7 21-23 8Z'/>",
            "soil" => "<path fill='#987354' d='m5 30 10-12 19-4 10 20-15 8-21-1Z'/>",
            "sand" => "<path fill='#dbc58b' d='m5 35 14-22 12 2 13 22-20 6Z'/>",
            "iron" => "<path fill='#b9ccd5' d='m8 18 25-8 10 10-5 15-25 7-8-10Z'/><path d='m8 18 10 10 25-8M18 28l-5 14'/>",
            "coal" => "<path fill='#48515e' d='m5 29 10-17 11 3 9-6 9 19-6 12-21 3Z'/><path d='m15 12 7 19 13-22M22 31l16 9'/>",
            "sword" => "<path fill='#b5d2da' d='m38 5 5 5-22 25-7-7Z'/><path stroke='#dfb66b' stroke-width='5' d='m10 24 14 14M8 41l9-10'/>",
            "door" => "<path fill='#b28d5c' d='M10 43V5h28v38Z'/><path fill='#665340' d='M16 11h16v32H16Z'/><circle fill='#efd28c' cx='27' cy='27' r='2'/>",
            "furnace" or "stove" => "<path fill='#8a9399' d='M9 7h30v36H9Z'/><path fill='#313941' d='M14 12h20v9H14zm0 14h20v13H14Z'/><path fill='#e6a15b' d='m18 35 3-11 4 7 4-6 2 10Z'/>",
            "anvil" => "<path fill='#9db0ba' d='M5 12h38v7l-13 5-2 10 8 5v4H13v-4l7-5-2-10-13-5Z'/>",
            "stairs" or "logstairs" or "plankstairs" or "cobblestairs" or "brickstairs" => "<path fill='#a7b6bb' d='M5 40V30h12V19h12V8h14v32Z'/><path stroke='#e7c27d' d='m7 21 23-16m-8 0h8v8'/>",
            "dig" or "pickaxe" => "<path fill='#ad8a56' stroke-width='2.5' d='m17 13 27 26q2 3-2 5-2 1-4-1L12 18Z'/><path fill='#899b9f' stroke-width='2.5' d='M4 39C1 15 17 1 40 7L40 10C21 9 11 19 7 39Z'/><path stroke='#cad0c2' d='M8 24Q16 8 34 8'/>",
            "digstairs" => "<path fill='#9ca9ae' d='M7 9h35v32H7Z'/><path fill='#262e36' d='M12 14h9v9h10v9h11v9H12Z'/>",
            "erase" => "<g fill='#7b9e77' stroke-width='2.3'><path d='M5 31h18v12H5Zm18 0h20v12H23ZM15 19h18v12H15Zm18 0h10v12H33ZM29 7h14v12H29Z'/><path fill='#a2bf8c' d='m5 5 15-2 1 10-15 2Z'/></g><path stroke='#c3d1ae' d='M8 34h11m7 0h13M18 22h11'/>",
            "cancel" => "<circle fill='#b94f3d' stroke-width='2.5' cx='24' cy='24' r='21'/><circle fill='#26343d' stroke-width='2.5' cx='24' cy='24' r='13'/><path fill='#dd6c51' stroke-width='2.5' d='m8 34 26-26 6 6-26 26Z'/>",
            "foundation" or "blueprint" => "<path fill='#466f86' d='M6 6h36v36H6Z'/><path stroke='#c5dce2' d='M12 12h24v24H12ZM24 12v24M12 24h24'/>",
            "rotate" => "<path stroke='#e2c083' stroke-width='4' d='M36 16a15 15 0 1 0 2 19'/><path fill='#e2c083' d='m29 17 14 3-3-14Z'/>",
            "floor" => "<path fill='#a9b9bd' d='m5 27 20-11 18 11-20 12Z'/><path d='M5 27v6l18 11 20-11v-6'/>",
            "ceiling" => "<path fill='#a9b9bd' d='m5 14 20-10 18 10-20 11Z'/><path d='M8 21v21m32-21v21'/>",
            "map" => "<path fill='#81a591' d='m5 10 13-5 13 5 12-5v33l-12 5-13-5-13 5Z'/><path d='M18 5v33m13-28v33'/>",
            "layers" => "<path fill='#a5b9c2' d='m5 15 19-10 19 10-19 10Z'/><path d='m5 25 19 10 19-10M5 35l19 10 19-10'/>",
            "down" => "<path fill='#c9d6d9' d='M19 7h10v20h11L24 43 8 27h11Z'/>",
            "up" => "<path fill='#c9d6d9' d='M19 41h10V21h11L24 5 8 21h11Z'/>",
            "play" => "<path fill='#c1d1c1' d='m13 7 27 17-27 17Z'/>",
            "pause" => "<path fill='#e2bf7f' d='M10 8h10v32H10zm18 0h10v32H28Z'/>",
            "fast" => "<path fill='#c1d1c1' d='m5 9 18 15L5 39Zm20 0 18 15-18 15Z'/>",
            "dev" => "<path stroke='#a5b7c6' stroke-width='6' d='M9 39 34 14'/><path fill='#a5b7c6' d='M28 5v12h12l-8 9-12-5-3-11Z'/>",
            "console" => "<path fill='#27343f' d='M4 8h40v32H4Z'/><path stroke='#a4d7b0' stroke-width='3' d='m11 16 8 8-8 8m13 0h12'/>",
            _ => "<path fill='#9eb1bb' d='m24 4 19 11v22L24 46 5 37V15Z'/><path d='m5 15 19 11 19-11M24 26v20'/>"
        };
        var img=new Image();
        img.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48' viewBox='0 0 48 48'><g stroke='#1c252c' stroke-width='2' stroke-linejoin='round' stroke-linecap='round' fill='none'>"+shape+"</g></svg>");
        texture=ImageTexture.CreateFromImage(img);Cache[name]=texture;return texture;
    }
}
