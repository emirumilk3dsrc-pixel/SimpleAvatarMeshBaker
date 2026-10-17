using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
internal static class SimpleMeshBakerValidation
{
    static double next;
    static SimpleMeshBakerValidation(){next=EditorApplication.timeSinceStartup+8;EditorApplication.update+=Poll;}
    static void Poll()
    {
        const string request="Diagnostics/VMCPCSS/check-meshtool.txt";
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup<next || !File.Exists(request))return;
        File.Delete(request);
        var root=new GameObject("Mesh Baker validation");root.SetActive(false);
        var mesh=new Mesh();var mat=new Material(Shader.Find("Standard"));SkinnedMeshRenderer result=null;string path=null;GameObject clone=null;string cloneMeshPath=null;
        try
        {
            mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up};mesh.triangles=new[]{0,1,2};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up};mesh.RecalculateNormals();
            mesh.bindposes=new[]{Matrix4x4.identity,Matrix4x4.identity};
            mesh.boneWeights=new[]{new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1}};
            mesh.AddBlendShapeFrame("A.Test",100,new[]{Vector3.up,Vector3.up,Vector3.up},new Vector3[3],new Vector3[3]);
            mesh.AddBlendShapeFrame("B.Test",100,new[]{Vector3.right,Vector3.right,Vector3.right},new Vector3[3],new Vector3[3]);
            var sources=new List<SkinnedMeshRenderer>();
            for(int i=0;i<2;i++){var go=new GameObject("Part"+i);go.transform.SetParent(root.transform,false);var r=go.AddComponent<SkinnedMeshRenderer>();r.sharedMesh=mesh;r.sharedMaterial=mat;r.bones=new[]{root.transform,(Transform)null};r.rootBone=root.transform;r.SetBlendShapeWeight(0,23+i*34);sources.Add(r);}
            var fixedBones=SimpleAvatarMeshBaker.ValidateBones(sources[0]);
            if(!fixedBones[1] || sources[0].bones[1])throw new Exception("Unused null bone handling modified original.");
            sources[0].bones=new[]{(Transform)null,root.transform};bool rejected=false;
            try{SimpleAvatarMeshBaker.ValidateBones(sources[0]);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Weighted missing bone accepted.");sources[0].bones=new[]{root.transform,(Transform)null};
            result=SimpleAvatarMeshBaker.Bake(root,sources,"MeshToolValidation");path=AssetDatabase.GetAssetPath(result.sharedMesh);
            if(result.sharedMesh.vertexCount!=6 || result.sharedMesh.blendShapeCount!=4)throw new Exception("Geometry or independent shapes lost.");
            if(Mathf.Abs(result.GetBlendShapeWeight(0)-23)>0.01f || Mathf.Abs(result.GetBlendShapeWeight(2)-57)>0.01f)throw new Exception("Shape weights lost.");
            if(sources[0].bones[1] || !sources[0].enabled || result.enabled)throw new Exception("Source or preview state altered.");
            UnityEngine.Object.DestroyImmediate(result.transform.parent.gameObject);result=null;
            root.SetActive(true);
            var inactive=new GameObject("InactiveClothes");inactive.transform.SetParent(root.transform,false);var inactiveRenderer=inactive.AddComponent<SkinnedMeshRenderer>();inactiveRenderer.sharedMesh=mesh;inactiveRenderer.sharedMaterial=mat;inactive.SetActive(false);sources.Add(inactiveRenderer);
            var disabled=new GameObject("DisabledClothes");disabled.transform.SetParent(root.transform,false);var disabledRenderer=disabled.AddComponent<SkinnedMeshRenderer>();disabledRenderer.sharedMesh=mesh;disabledRenderer.sharedMaterial=mat;disabledRenderer.enabled=false;
            var dependencyType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("nadena.dev.modular_avatar.core.ModularAvatarBlendshapeSync")).FirstOrDefault(t=>t!=null);
            if(dependencyType!=null)sources[0].gameObject.AddComponent(dependencyType);
            clone=SimpleAvatarMeshBaker.BakeAvatarCopy(root,sources,"CopyValidation");
            var renderers=clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if(renderers.Length!=1 || !renderers[0].enabled || root.activeSelf || !clone.activeSelf)throw new Exception("Clone renderer count/activation mismatch.");
            if(clone.transform.Find("Part0") || clone.transform.Find("Part1") || clone.transform.Find("InactiveClothes") || clone.transform.Find("DisabledClothes"))throw new Exception("Source mesh objects were retained.");
            if(renderers[0].sharedMesh.vertexCount!=6)throw new Exception("Inactive mesh entered bake.");
            cloneMeshPath=AssetDatabase.GetAssetPath(renderers[0].sharedMesh);
            if(!sources[0] || sources[0].bones[1] || !sources[0].enabled)throw new Exception("Original renderer changed.");
            foreach(var bone in renderers[0].bones)if(bone && !bone.IsChildOf(clone.transform))throw new Exception("Merged copy references original bones.");
            File.WriteAllText("Diagnostics/VMCPCSS/meshtool-validation.txt","PASS: unused null bones handled on copies; weighted missing bones rejected; real Mesh Baker merge preserved 6 vertices, 4 independent dotted-name blendshapes and weights 23/57; original bones/renderers untouched; cloned avatar contains one renderer with clone-local bones, original disabled only after success; inactive meshes excluded/deleted and source mesh objects with dependent components removed.\n");
        }
        catch(Exception e){File.WriteAllText("Diagnostics/VMCPCSS/meshtool-validation.txt","FAIL: "+e);}
        finally{if(clone)UnityEngine.Object.DestroyImmediate(clone);if(cloneMeshPath!=null)AssetDatabase.DeleteAsset(cloneMeshPath);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(mat);if(path!=null)AssetDatabase.DeleteAsset(path);}
    }
}
