using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class SimpleAvatarMeshBaker : EditorWindow
{
    [SerializeField] GameObject avatar;
    [SerializeField] List<SkinnedMeshRenderer> sources=new List<SkinnedMeshRenderer>();
    [SerializeField] SkinnedMeshRenderer result;
    [SerializeField] List<SkinnedMeshRenderer> bakedSources=new List<SkinnedMeshRenderer>();
    [SerializeField] List<bool> previousEnabled=new List<bool>();
    [SerializeField] string outputName="CombinedClothes";
    [SerializeField] bool applied;
    [SerializeField] GameObject originalAvatar, clonedAvatar;
    [SerializeField] bool originalActive;
    Vector2 scroll;
    string status;
    [MenuItem("Tools/Simple Avatar Mesh Baker")]
    public static void Open(){GetWindow<SimpleAvatarMeshBaker>("Simple Avatar Mesh Baker");}
    void OnEnable()
    {
        titleContent=new GUIContent("Simple Avatar Mesh Baker");
        MeshBakerBridge.Check();
    }
    void RefreshAvatar(GameObject root)
    {
        avatar=root;
        // Completed previews remain in the scene; never toggle an old avatar on root changes.
        result=null;clonedAvatar=null;originalAvatar=null;applied=false;
        bakedSources.Clear();previousEnabled.Clear();sources.Clear();scroll=Vector2.zero;
        if(root)sources.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r=>r.sharedMesh && IsActiveMesh(r,root.transform)));
        status="Avatar mesh list refreshed. Remove meshes you do not want to combine.";
    }
    void OnGUI()
    {
        if(!MeshBakerBridge.Available)
        {
            var red=new GUIStyle(EditorStyles.wordWrappedLabel);
            red.normal.textColor=Color.red;
            EditorGUILayout.LabelField("Mesh Baker is not imported or is incompatible. Mesh Baker is required to use this tool.",red);
            EditorGUILayout.LabelField(MeshBakerBridge.Error,EditorStyles.wordWrappedLabel);
            var link=new GUIStyle(EditorStyles.linkLabel);
            link.normal.textColor=Color.red;link.hover.textColor=Color.red;link.active.textColor=Color.red;
            if(GUILayout.Button("Get Mesh Baker (Digital Opus)",link))Application.OpenURL("https://digitalopus.ca/site/mesh-baker/");
            if(GUILayout.Button("Check Mesh Baker Again")){MeshBakerBridge.Check();Repaint();}
            return;
        }
        EditorGUILayout.HelpBox("Select clothing meshes to combine. Materials and individual blendshapes are preserved. The original avatar is retained.",MessageType.Info);
        using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if(GUILayout.Button("Reset (Restore Original / Clear Selection)"))ResetTool();
        var newAvatar=(GameObject)EditorGUILayout.ObjectField("Avatar Root",avatar,typeof(GameObject),true);
        if(newAvatar!=avatar)RefreshAvatar(newAvatar);
        outputName=EditorGUILayout.TextField("Output Name",outputName);
        if(GUILayout.Button("Add Selected Active Meshes (Include Children)"))
        {
            foreach(var go in Selection.gameObjects)
                foreach(var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(r!=result && r.enabled && r.gameObject.activeInHierarchy && !sources.Contains(r))sources.Add(r);
            if(!avatar && sources.Count>0){var a=sources[0].GetComponentInParent<Animator>();if(a)avatar=a.gameObject;}
        }
        scroll=EditorGUILayout.BeginScrollView(scroll);
        for(int i=0;i<sources.Count;i++)
        {
            EditorGUILayout.BeginHorizontal();
            sources[i]=(SkinnedMeshRenderer)EditorGUILayout.ObjectField(sources[i],typeof(SkinnedMeshRenderer),true);
            if(GUILayout.Button("Remove",GUILayout.Width(45))){sources.RemoveAt(i--);}
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.LabelField("Selected Meshes",sources.Count+" meshes to 1");
        EditorGUILayout.HelpBox("Current blendshape weights are copied. Animation and physics renderer references are not remapped automatically. Caster-masked meshes must remain separate.",MessageType.None);
        using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || result))
            if(GUILayout.Button("Create Merged Preview (Keep Original Visible)"))
                try{result=Bake(avatar,sources,outputName);bakedSources=sources.ToList();previousEnabled=sources.Select(r=>r.enabled).ToList();applied=false;status="Created. Apply the result to preview it.";}
                catch(Exception e){status=e.Message;Debug.LogException(e);}
        using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || result || clonedAvatar))
            if(GUILayout.Button("Duplicate Avatar / Merge Active Meshes / Disable Original"))
                try
                {
                    originalActive=avatar && avatar.activeSelf;
                    var original=avatar;
                    var clone=BakeAvatarCopy(avatar,sources,outputName);
                    originalAvatar=original;clonedAvatar=clone;avatar=clone;sources.Clear();
                    Selection.activeGameObject=clone;
                    status="Created avatar copy. Merged source objects and inactive meshes were removed from the copy. Original avatar disabled.";
                }
                catch(Exception e){status=e.Message;Debug.LogException(e);}
        if(clonedAvatar)EditorGUILayout.HelpBox("Export the copy using the existing exporter. Reset disables the copy and restores the original. Check animation and physics references.",MessageType.Info);
        if(result)
        {
            EditorGUILayout.ObjectField("Merged Result",result,typeof(SkinnedMeshRenderer),true);
            if(GUILayout.Button(applied?"Restore Sources":"Apply Result / Hide Sources"))
            {
                Undo.RecordObjects(bakedSources.Cast<UnityEngine.Object>().Append(result).ToArray(),"Switch merged clothes");
                applied=!applied;result.enabled=applied;
                for(int i=0;i<bakedSources.Count;i++)if(bakedSources[i])bakedSources[i].enabled=applied?false:previousEnabled[i];
                EditorUtility.SetDirty(result);SceneView.RepaintAll();
            }
            if(GUILayout.Button("Select Result / Adjust Blendshapes"))Selection.activeGameObject=result.gameObject;
            EditorGUILayout.HelpBox("The result is created inside the avatar. Apply it before exporting. Use Next Group to combine another set.",MessageType.Info);
            if(GUILayout.Button("Keep Result / Next Group")){result=null;sources.Clear();bakedSources.Clear();previousEnabled.Clear();applied=false;}
        }
        if(!string.IsNullOrEmpty(status))EditorGUILayout.HelpBox(status,MessageType.Info);
    }
    void ResetTool()
    {
        if(clonedAvatar){Undo.RecordObject(clonedAvatar,"Disable baked avatar");clonedAvatar.SetActive(false);}
        if(originalAvatar){Undo.RecordObject(originalAvatar,"Restore original avatar");originalAvatar.SetActive(originalActive);}
        clonedAvatar=null;originalAvatar=null;
        if(result)
        {
            var targets=bakedSources.Where(r=>r).Cast<UnityEngine.Object>().Append(result).ToArray();
            Undo.RecordObjects(targets,"Reset mesh baker preview");
            result.enabled=false;EditorUtility.SetDirty(result);
            for(int i=0;i<bakedSources.Count && i<previousEnabled.Count;i++)
                if(bakedSources[i]){bakedSources[i].enabled=previousEnabled[i];EditorUtility.SetDirty(bakedSources[i]);}
        }
        avatar=null;result=null;sources.Clear();bakedSources.Clear();previousEnabled.Clear();
        outputName="CombinedClothes";applied=false;scroll=Vector2.zero;
        status="Reset complete. Original visibility restored; generated mesh assets retained.";
        SceneView.RepaintAll();
    }
    internal static GameObject BakeAvatarCopy(GameObject original,List<SkinnedMeshRenderer> selected,string label)
    {
        if(!original || EditorUtility.IsPersistent(original))throw new InvalidOperationException("Choose an avatar root in the scene.");
        if(selected.Count<2 || selected.Any(r=>!r || !r.transform.IsChildOf(original.transform)))throw new InvalidOperationException("Select at least two meshes inside the avatar root.");
        selected=selected.Where(r=>IsActiveMesh(r,original.transform)).Distinct().ToList();
        if(selected.Count<2)throw new InvalidOperationException("At least two active meshes are required.");
        Func<Transform,List<int>> pathOf=t=>{var path=new List<int>();while(t!=original.transform){path.Add(t.GetSiblingIndex());t=t.parent;}path.Reverse();return path;};
        var paths=selected.Select(r=>pathOf(r.transform)).ToArray();
        var inactivePaths=original.GetComponentsInChildren<Renderer>(true)
            .Where(r=>(r is SkinnedMeshRenderer || r is MeshRenderer) && !IsActiveMesh(r,original.transform))
            .Select(r=>pathOf(r.transform)).ToArray();
        GameObject clone=null;SkinnedMeshRenderer merged=null;string meshPath=null;
        try
        {
            clone=Instantiate(original,original.transform.parent);clone.name=original.name+"_Merged";clone.SetActive(false);
            var copies=paths.Select(path=>{var t=clone.transform;foreach(int index in path)t=t.GetChild(index);return t.GetComponent<SkinnedMeshRenderer>();}).ToList();
            var inactiveObjects=inactivePaths.Select(path=>{var t=clone.transform;foreach(int index in path)t=t.GetChild(index);return t.gameObject;}).ToList();
            merged=Bake(clone,copies,label);meshPath=AssetDatabase.GetAssetPath(merged.sharedMesh);
            merged.enabled=true;
            var remove=copies.Select(r=>r.gameObject).Concat(inactiveObjects).Distinct().ToArray();
            var retainedBones=new HashSet<Transform>(clone.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r=>!remove.Contains(r.gameObject)).SelectMany(r=>r.bones.Concat(new[]{r.rootBone})).Where(t=>t));
            foreach(var go in remove)
                if(go==clone || retainedBones.Contains(go.transform) || go.GetComponent<Animator>())
                    throw new InvalidOperationException(go.name+": This object is also a bone or avatar root and cannot be deleted.");
            // Delete mesh objects and their dependent components together. Preserve child bones/objects.
            foreach(var go in remove.OrderByDescending(g=>Depth(g.transform)))
            {
                while(go.transform.childCount>0)
                {
                    var child=go.transform.GetChild(0);bool keepActive=child.gameObject.activeSelf && go.activeSelf;
                    child.SetParent(go.transform.parent,true);child.gameObject.SetActive(keepActive);
                }
                DestroyImmediate(go);
            }
            // Recollect after removal so shadow correction never targets deleted renderers.
            foreach(var source in clone.GetComponentsInChildren<VMCPCSS.VMCPCSSShadowRenderer>(true))
                VMCPCSS.Editor.VMCPCSSSetupEditor.SetupLight(source.gameObject,clone);
            Undo.RegisterCreatedObjectUndo(clone,"Create merged avatar copy");
            Undo.RecordObject(original,"Disable original avatar");original.SetActive(false);clone.SetActive(true);
            return clone;
        }
        catch
        {
            if(clone)DestroyImmediate(clone);
            if(meshPath!=null)AssetDatabase.DeleteAsset(meshPath);
            throw;
        }
    }
    static int Depth(Transform t){int depth=0;while(t){depth++;t=t.parent;}return depth;}
    static bool IsActiveMesh(Renderer renderer,Transform root)
    {
        if(!renderer.enabled)return false;
        for(var t=renderer.transform;t && t!=root;t=t.parent)if(!t.gameObject.activeSelf)return false;
        return true;
    }
    internal static Transform[] ValidateBones(SkinnedMeshRenderer source)
    {
        var bones=source.bones;
        var mesh=source.sharedMesh;
        var used=new HashSet<int>();
        using(var weights=mesh.GetAllBoneWeights())
            foreach(var weight in weights)
                if(weight.weight>0)used.Add(weight.boneIndex);
        var broken=used.Where(i=>i<0 || i>=bones.Length || !bones[i]).OrderBy(i=>i).ToArray();
        if(broken.Length>0)throw new InvalidOperationException(source.name+": Missing weighted bones. Indices: "+string.Join(", ",broken)+". Restore the source bone references before merging.");
        if(bones.Length!=mesh.bindposes.Length)throw new InvalidOperationException(source.name+": Bone count differs from bind poses. Check the source skin settings.");
        // Zero-weight entries cannot deform vertices. Substitute only in the bake copy.
        for(int i=0;i<bones.Length;i++)if(!bones[i])bones[i]=source.rootBone?source.rootBone:source.transform;
        return bones;
    }
    internal static SkinnedMeshRenderer Bake(GameObject root,List<SkinnedMeshRenderer> inputs,string label)
    {
        if(!root || EditorUtility.IsPersistent(root))throw new InvalidOperationException("Choose an avatar root in the scene.");
        if(inputs.Count<2 || inputs.Any(r=>!r) || inputs.Distinct().Count()!=inputs.Count)throw new InvalidOperationException("Select at least two distinct meshes.");
        foreach(var r in inputs)
        {
            if(!r.transform.IsChildOf(root.transform) || EditorUtility.IsPersistent(r))throw new InvalidOperationException("All meshes must belong to the specified avatar.");
            if(!r.sharedMesh || !r.sharedMesh.isReadable)throw new InvalidOperationException(r.name+": A readable mesh is required.");
            if(r.sharedMaterials.Any(m=>!m))throw new InvalidOperationException(r.name+": Check empty material slots.");
            if(r.GetComponent<VMCPCSS.VMCPCSSShadowCasterMask>())throw new InvalidOperationException(r.name+": Keep caster-masked meshes separate.");
        }
        var validatedBones=inputs.Select(ValidateBones).ToArray();
        var copies=new List<GameObject>();
        var temporaryMeshes=new List<Mesh>();
        MeshBakerBridge.Require();
        Component baker=null;object combiner=null;
        var helper=new GameObject("Temporary Mesh Baker");helper.hideFlags=HideFlags.HideAndDontSave;
        GameObject generated=null;Mesh asset=null;string assetPath=null;
        try
        {
            for(int i=0;i<inputs.Count;i++)
            {
                var original=inputs[i];
                var go=new GameObject(original.name+"_BakeCopy");copies.Add(go);
                go.hideFlags=HideFlags.HideAndDontSave;
                go.transform.SetParent(original.transform.parent,false);
                go.transform.localPosition=original.transform.localPosition;
                go.transform.localRotation=original.transform.localRotation;
                go.transform.localScale=original.transform.localScale;
                var copy=go.AddComponent<SkinnedMeshRenderer>();EditorUtility.CopySerialized(original,copy);
                copy.bones=validatedBones[i];copy.enabled=false;
                if(original.sharedMesh.blendShapeCount>0)
                {
                    var src=original.sharedMesh;var renamed=Instantiate(src);temporaryMeshes.Add(renamed);renamed.ClearBlendShapes();
                    var dv=new Vector3[src.vertexCount];var dn=new Vector3[src.vertexCount];var dt=new Vector3[src.vertexCount];
                    for(int shape=0;shape<src.blendShapeCount;shape++)
                    {
                        // Mesh Baker strips prefixes before the final dot. Avoid dots and include a unique index.
                        string unique="Part"+i+"_Shape"+shape+"_"+src.GetBlendShapeName(shape).Replace('.', '_');
                        for(int frame=0;frame<src.GetBlendShapeFrameCount(shape);frame++)
                        {
                            src.GetBlendShapeFrameVertices(shape,frame,dv,dn,dt);
                            renamed.AddBlendShapeFrame(unique,src.GetBlendShapeFrameWeight(shape,frame),dv,dn,dt);
                        }
                    }
                    copy.sharedMesh=renamed;
                    for(int shape=0;shape<src.blendShapeCount;shape++)copy.SetBlendShapeWeight(shape,original.GetBlendShapeWeight(shape));
                }
            }
            baker=helper.AddComponent(MeshBakerBridge.BakerType);
            MeshBakerBridge.Set(baker,"useObjsToMeshFromTexBaker",false);
            ((List<GameObject>)MeshBakerBridge.Call(baker,"GetObjectsToCombine")).AddRange(copies);
            combiner=MeshBakerBridge.Get(baker,"meshCombiner");
            MeshBakerBridge.Set(combiner,"renderType","skinnedMeshRenderer");
            MeshBakerBridge.Set(combiner,"doBlendShapes",true);
            MeshBakerBridge.Set(combiner,"smrMergeBlendShapesWithSameNames",false);
            MeshBakerBridge.Set(combiner,"outputOption","bakeIntoSceneObject");
            if(!(bool)MeshBakerBridge.BakeMethod.Invoke(null,new object[]{baker,false}))throw new InvalidOperationException("Mesh Baker failed. Check the Console for details.");
            generated=(GameObject)MeshBakerBridge.Get(combiner,"resultSceneObject");
            var target=MeshBakerBridge.Get(combiner,"targetRenderer") as SkinnedMeshRenderer;
            if(!target || !target.sharedMesh)throw new InvalidOperationException("No merged mesh was created.");
            generated.transform.SetParent(root.transform,true);
            generated.name=string.IsNullOrWhiteSpace(label)?"CombinedClothes":label;
            target.gameObject.layer=inputs[0].gameObject.layer;
            asset=Instantiate(target.sharedMesh);asset.name=generated.name;
            target.sharedMesh=asset;
            var mapComponent=target.GetComponent(MeshBakerBridge.MapType);
            var map=mapComponent?MeshBakerBridge.Call(mapComponent,"GetMap"):null;
            var mapSources=map==null?null:(GameObject[])MeshBakerBridge.Get(map,"srcGameObject");
            if(mapSources!=null)
            {
                var dstIndices=(int[])MeshBakerBridge.Get(map,"blendShapeIdx");
                var srcIndices=(int[])MeshBakerBridge.Get(map,"srcBlendShapeIdx");
                for(int i=0;i<mapSources.Length;i++)
                    target.SetBlendShapeWeight(dstIndices[i],mapSources[i].GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(srcIndices[i]));
            }
            if(mapComponent)DestroyImmediate(mapComponent);
            target.enabled=false;target.updateWhenOffscreen=true;
            const string folder="Assets/SimpleMeshBakerGenerated";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets","SimpleMeshBakerGenerated");
            assetPath=AssetDatabase.GenerateUniqueAssetPath(folder+"/CombinedClothes.asset");AssetDatabase.CreateAsset(asset,assetPath);AssetDatabase.SaveAssets();
            Undo.RegisterCreatedObjectUndo(generated,"Create combined clothes");
            return target;
        }
        catch
        {
            if(!generated && combiner!=null)generated=(GameObject)MeshBakerBridge.Get(combiner,"resultSceneObject");
            if(generated)DestroyImmediate(generated);
            if(assetPath!=null)AssetDatabase.DeleteAsset(assetPath);else if(asset)DestroyImmediate(asset);
            throw;
        }
        finally{DestroyImmediate(helper);foreach(var copy in copies)if(copy)DestroyImmediate(copy);foreach(var mesh in temporaryMeshes)if(mesh)DestroyImmediate(mesh);}
    }

    // No compile-time dependency on Mesh Baker: missing packages must not break this window.
    internal static class MeshBakerBridge
    {
        internal static bool Available {get;private set;}
        internal static string Error {get;private set;}
        internal static Type BakerType,MapType;
        internal static MethodInfo BakeMethod;
        static bool checkedOnce;
        static Type Find(string name)
        {
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {var type=assembly.GetType(name,false);if(type!=null)return type;}
            return null;
        }
        internal static void Check()
        {
            checkedOnce=true;Available=false;Error="";
            BakerType=Find("MB3_MeshBaker");
            MapType=Find("DigitalOpus.MB.Core.MB_BlendShape2CombinedMap");
            var editor=Find("MB3_MeshBakerEditorFunctions");
            BakeMethod=editor==null?null:editor.GetMethods(BindingFlags.Public|BindingFlags.Static)
                .FirstOrDefault(m=>m.Name=="BakeIntoCombined" && m.GetParameters().Length==2 &&
                    BakerType!=null && m.GetParameters()[0].ParameterType.IsAssignableFrom(BakerType) &&
                    m.GetParameters()[1].ParameterType==typeof(bool).MakeByRefType());
            Available=BakerType!=null && MapType!=null && BakeMethod!=null;
            if(!Available)Error="Import Mesh Baker, wait for compilation, then click Check Mesh Baker Again.";
        }
        internal static void Require()
        {if(!checkedOnce)Check();if(!Available)throw new InvalidOperationException("Mesh Baker is not imported or is incompatible.");}
        internal static object Get(object obj,string name)
        {
            // Some Mesh Baker overrides expose only a setter; the getter lives on the base type.
            for(var t=obj.GetType();t!=null;t=t.BaseType)
            {
                var p=t.GetProperty(name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if(p!=null && p.GetGetMethod()!=null)return p.GetValue(obj,null);
                var f=t.GetField(name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if(f!=null)return f.GetValue(obj);
            }
            throw new MissingMemberException(obj.GetType().FullName,"Readable "+name);
        }
        internal static void Set(object obj,string name,object value)
        {
            var t=obj.GetType();var p=t.GetProperty(name);var f=t.GetField(name);
            var type=p!=null?p.PropertyType:f.FieldType;
            if(type.IsEnum)value=Enum.Parse(type,(string)value);
            if(p!=null)p.SetValue(obj,value,null);else f.SetValue(obj,value);
        }
        internal static object Call(object obj,string name)
        {return obj.GetType().GetMethod(name,Type.EmptyTypes).Invoke(obj,null);}
    }
}
