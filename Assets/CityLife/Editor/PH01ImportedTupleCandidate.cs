#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World.Editor
{
    /// <summary>
    /// Controlled candidate: replace extracted PH01 P/N/UV/T with actual imported tuples.
    /// No source/importer writes, no render, no
    /// ConfigureSourceRosettes call, no tangent/normal recalculation after imported tuples copy.
    /// </summary>
    public static class PH01ImportedTupleCandidate
    {
        const float CellSize=.0001f, PositionTolerance=.00002f, UvTolerance=.00001f;
        const float NormalDot=.9999f, EquivalentTangentDot=.99999f;

        /// <summary>
        /// Caller owns returned A-E meshes. On unmatched or ambiguous material tangent tuples,
        /// throws after populating report; caller can retain report before changing any live mesh.
        /// Exact-tuple dedup is optional and never merges a UV/normal/tangent seam.
        /// </summary>
        public static Mesh[] CreateRosettes(bool exactTupleDedup,out Report report)
        {
            Mesh[] originals;
            Mesh[] result=CreateRosettes(exactTupleDedup,out report,out originals);
            foreach(Mesh mesh in originals)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
            return result;
        }

        public static Mesh[] CreateRosettes(bool exactTupleDedup,out Report report,out Mesh[] originalSubsets)
        {
            report=new Report{exactTupleDedup=exactTupleDedup};
            originalSubsets=new Mesh[5];
            byte[] bytes=File.ReadAllBytes(KokerboomSourceRosettes.SourceAssetPath);
            using(var sha=SHA256.Create())report.sourceSha256=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            Require(report.sourceSha256==KokerboomSourceRosettes.SourceSha256,"Pinned PH01 source hash changed.");
            var importer=AssetImporter.GetAtPath(KokerboomSourceRosettes.SourceAssetPath) as ModelImporter;
            if(importer!=null){report.normalImportMode=importer.importNormals.ToString();report.tangentImportMode=importer.importTangents.ToString();}
            List<int[]> importedTriangles;
            List<ImportedCorner> imported=ReadImported(report,out importedTriangles);
            var grid=new Dictionary<Vector3Int,List<int>>();
            for(int i=0;i<imported.Count;i++)
            {
                Vector3Int key=Cell(imported[i].Position);
                if(!grid.TryGetValue(key,out List<int> list)){list=new List<int>();grid.Add(key,list);}list.Add(i);
            }
            Mesh[] baseline=KokerboomSourceRosettes.CreateRosettes(false);
            var output=new Mesh[baseline.Length];
            try
            {
                var description=KokerboomSourceRosettes.Describe();
                report.groups=new GroupReport[baseline.Length];
                var maps=new int[baseline.Length][];
                var memberships=new HashSet<int>[baseline.Length];
                for(int group=0;group<baseline.Length;group++)
                {
                    Mesh mesh=baseline[group];Vector3[] p=mesh.vertices,n=mesh.normals;Vector2[] uv=mesh.uv;Vector4[] tangents=mesh.tangents;
                    Vector3 root=description.groups[group].sourceRootUnity;
                    var r=new GroupReport{id=description.groups[group].id,inputCornerVertices=p.Length,sourceRootUnity=root};
                    report.groups[group]=r;maps[group]=new int[p.Length];
                    memberships[group]=new HashSet<int>();
                    for(int corner=0;corner<p.Length;corner++)
                    {
                        Vector3 world=p[corner]+root;Vector3Int center=Cell(world);
                        var matches=new List<int>();
                        for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                        {
                            if(!grid.TryGetValue(center+new Vector3Int(x,y,z),out List<int> list))continue;
                            foreach(int index in list)
                            {
                                ImportedCorner c=imported[index];
                                if((world-c.Position).sqrMagnitude<=PositionTolerance*PositionTolerance && (uv[corner]-c.Uv).sqrMagnitude<=UvTolerance*UvTolerance && Vector3.Dot(n[corner].normalized,c.Normal)>=NormalDot)matches.Add(index);
                            }
                        }
                        if(matches.Count==0)
                        {
                            r.unmatchedCorners++;maps[group][corner]=-1;
                            AddIssue(r,corner,"No imported P/N/UV match",world,matches,imported);continue;
                        }
                        // Stable closest P/N/UV choice is allowed ONLY when every candidate's
                        // tangent basis agrees. Never choose by the baseline's incorrect tangent.
                        int best=matches.OrderBy(i=>(imported[i].Position-world).sqrMagnitude)
                            .ThenBy(i=>(imported[i].Uv-uv[corner]).sqrMagnitude)
                            .ThenByDescending(i=>Vector3.Dot(imported[i].Normal,n[corner].normalized)).ThenBy(i=>i).First();
                        ImportedCorner selected=imported[best];bool ambiguous=false;
                        foreach(int index in matches)
                        {
                            ImportedCorner other=imported[index];
                            if(Vector3.Dot(Tangent3(selected.Tangent),Tangent3(other.Tangent))<EquivalentTangentDot || Mathf.Sign(selected.Tangent.w)!=Mathf.Sign(other.Tangent.w)){ambiguous=true;break;}
                        }
                        if(ambiguous)
                        {
                            r.ambiguousCorners++;maps[group][corner]=-1;
                            AddIssue(r,corner,"Multiple P/N/UV matches have materially different imported tangents; requires topology-aware disambiguation",world,matches,imported);continue;
                        }
                        maps[group][corner]=best;r.matchedCorners++;
                        foreach(int index in matches)memberships[group].Add(index);
                        r.maximumPositionDelta=Mathf.Max(r.maximumPositionDelta,Vector3.Distance(world,selected.Position));
                        r.maximumUvDelta=Mathf.Max(r.maximumUvDelta,Vector2.Distance(uv[corner],selected.Uv));
                        r.minimumSourceNormalDot=Mathf.Min(r.minimumSourceNormalDot,Vector3.Dot(n[corner].normalized,selected.Normal));
                        if(tangents.Length==p.Length)
                        {
                            float dot=Vector3.Dot(Tangent3(tangents[corner]).normalized,Tangent3(selected.Tangent));
                            r.minimumBaselineTangentDot=Mathf.Min(r.minimumBaselineTangentDot,dot);
                            if(dot<NormalDot)r.baselineTangentDirectionsChanged++;
                            if(Mathf.Sign(tangents[corner].w)!=Mathf.Sign(selected.Tangent.w))r.baselineTangentHandednessChanged++;
                        }
                    }
                }
                report.mappingComplete=report.groups.All(g=>g.unmatchedCorners==0&&g.ambiguousCorners==0);
                Require(report.mappingComplete,"Imported tuple mapping is incomplete/ambiguous. Retain the report and inspect topology; no candidate meshes were produced.");
                for(int group=0;group<baseline.Length;group++)
                {
                    var p=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var t=new List<Vector4>();
                    var remap=new int[maps[group].Length];
                    var exact=new Dictionary<Tuple<Vector3,Vector3,Vector2,Vector4>,int>();
                    Vector3 root=description.groups[group].sourceRootUnity;
                    for(int corner=0;corner<maps[group].Length;corner++)
                    {
                        ImportedCorner c=imported[maps[group][corner]];
                        // Imported tuple is already in Unity world/metre coordinates. Subtract
                        // only the rosette pivot; translation changes neither N nor tangent4.
                        Vector3 local=c.Position-root;
                        var tuple=Tuple.Create(local,c.Normal,c.Uv,c.Tangent);
                        if(exactTupleDedup&&exact.TryGetValue(tuple,out int existing)){remap[corner]=existing;continue;}
                        remap[corner]=p.Count;if(exactTupleDedup)exact.Add(tuple,p.Count);
                        p.Add(local);n.Add(c.Normal);uv.Add(c.Uv);t.Add(c.Tangent);
                    }
                    int[] indices=baseline[group].triangles;
                    for(int i=0;i<indices.Length;i++)indices[i]=remap[indices[i]];
                    var mesh=new Mesh{name="PH01 imported tuple candidate "+description.groups[group].id,indexFormat=IndexFormat.UInt32};
                    output[group]=mesh;
                    mesh.SetVertices(p);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.SetTangents(t);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
                    // Intentionally NO RecalculateTangents or RecalculateNormals here.
                    report.groups[group].outputVertices=p.Count;report.groups[group].outputTriangles=indices.Length/3;
                    VerifyCandidate(mesh,baseline[group],maps[group],remap,root,imported,report.groups[group]);
                    originalSubsets[group]=BuildOriginalSubset(imported,importedTriangles,memberships[group],maps[group],root,report.groups[group]);
                }
                report.numericChecksPassed=report.groups.All(g=>g.actualCornerTuplesPreserved&&g.triangleCornerOrderPreserved&&g.finiteUnitBasis);
                Require(report.numericChecksPassed,"Copied imported tuple or topology checks failed.");
                report.meshesCreated=true;return output;
            }
            catch
            {
                foreach(Mesh mesh in output)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
                foreach(Mesh mesh in originalSubsets)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
            finally{foreach(Mesh mesh in baseline)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);}
        }

        static List<ImportedCorner> ReadImported(Report report,out List<int[]> triangles)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(KokerboomSourceRosettes.SourceAssetPath);
            Require(asset!=null,"Imported PH01 asset unavailable.");
            var result=new List<ImportedCorner>();int meshId=0;triangles=new List<int[]>();
            foreach(MeshFilter filter in asset.GetComponentsInChildren<MeshFilter>(true).OrderBy(f=>HierarchyPath(f.transform),StringComparer.Ordinal))
            {
                Mesh mesh=filter.sharedMesh;Renderer renderer=filter.GetComponent<Renderer>();if(mesh==null||renderer==null)continue;
                using(var dataArray=MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    Mesh.MeshData data=dataArray[0];
                    Require(data.HasVertexAttribute(VertexAttribute.Normal)&&data.HasVertexAttribute(VertexAttribute.TexCoord0)&&data.HasVertexAttribute(VertexAttribute.Tangent),"Imported source lacks P/N/UV/T attributes.");
                    using(var p=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp))
                    using(var n=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp))
                    using(var uv=new NativeArray<Vector2>(data.vertexCount,Allocator.Temp))
                    using(var t=new NativeArray<Vector4>(data.vertexCount,Allocator.Temp))
                    {
                        data.GetVertices(p);data.GetNormals(n);data.GetUVs(0,uv);data.GetTangents(t);
                        var selected=new HashSet<int>();var localTriangles=new List<int[]>();Material[] materials=renderer.sharedMaterials;
                        for(int slot=0;slot<data.subMeshCount;slot++)
                        {
                            if(slot>=materials.Length||materials[slot]==null||materials[slot].name.IndexOf("leaf",StringComparison.OrdinalIgnoreCase)<0)continue;
                            SubMeshDescriptor sub=data.GetSubMesh(slot);Require(sub.topology==MeshTopology.Triangles,"Imported leaf topology is not triangles.");
                            using(var indices=new NativeArray<int>(sub.indexCount,Allocator.Temp))
                            {
                                data.GetIndices(indices,slot,true);foreach(int index in indices)selected.Add(index);
                                for(int j=0;j<indices.Length;j+=3)localTriangles.Add(new[]{indices[j],indices[j+1],indices[j+2]});
                            }
                        }
                        Matrix4x4 matrix=filter.transform.localToWorldMatrix,normalMatrix=matrix.inverse.transpose;float determinant=matrix.determinant;
                        Require(Mathf.Abs(determinant)>1e-12f,"Singular imported transform.");
                        var globalIndices=new Dictionary<int,int>();
                        foreach(int i in selected.OrderBy(i=>i))
                        {
                            Vector3 tangent=matrix.MultiplyVector(Tangent3(t[i])).normalized;
                            Vector3 normal=normalMatrix.MultiplyVector(n[i]).normalized;
                            Require(tangent.sqrMagnitude>.99f&&normal.sqrMagnitude>.99f&&Mathf.Abs(t[i].w)>.99f,"Invalid imported tangent/normal basis.");
                            globalIndices.Add(i,result.Count);
                            result.Add(new ImportedCorner{Position=matrix.MultiplyPoint3x4(p[i]),Normal=normal,Uv=uv[i],Tangent=new Vector4(tangent.x,tangent.y,tangent.z,t[i].w*Mathf.Sign(determinant)),MeshId=meshId,Vertex=i});
                        }
                        foreach(int[] triangle in localTriangles)triangles.Add(determinant<0
                            ?new[]{globalIndices[triangle[0]],globalIndices[triangle[2]],globalIndices[triangle[1]]}
                            :new[]{globalIndices[triangle[0]],globalIndices[triangle[1]],globalIndices[triangle[2]]});
                    }
                }
                meshId++;
            }
            report.importedLeafVertices=result.Count;Require(result.Count>0,"No imported leaf vertices found.");return result;
        }

        static void VerifyCandidate(Mesh mesh,Mesh baseline,int[] map,int[] remap,Vector3 root,List<ImportedCorner> imported,GroupReport report)
        {
            Vector3[] p=mesh.vertices,n=mesh.normals;Vector2[] uv=mesh.uv;Vector4[] t=mesh.tangents;
            report.actualCornerTuplesPreserved=true;report.finiteUnitBasis=true;
            for(int i=0;i<map.Length;i++)
            {
                ImportedCorner source=imported[map[i]];int target=remap[i];
                if(!p[target].Equals(source.Position-root)||!n[target].Equals(source.Normal)||!uv[target].Equals(source.Uv)||!t[target].Equals(source.Tangent))report.actualCornerTuplesPreserved=false;
                if(!Finite(p[target])||!Finite(n[target])||!Finite(Tangent3(t[target]))||!Finite(uv[target].x)||!Finite(uv[target].y)||!Finite(t[target].w)
                    ||Mathf.Abs(n[target].magnitude-1)>1e-4f||Mathf.Abs(Tangent3(t[target]).magnitude-1)>1e-4f||Mathf.Abs(Mathf.Abs(t[target].w)-1)>1e-4f)report.finiteUnitBasis=false;
            }
            int[] sourceIndices=baseline.triangles,indices=mesh.triangles;
            report.triangleCornerOrderPreserved=indices.Length==sourceIndices.Length;
            if(report.triangleCornerOrderPreserved)for(int i=0;i<indices.Length;i++)if(indices[i]!=remap[sourceIndices[i]]){report.triangleCornerOrderPreserved=false;break;}
        }

        static Mesh BuildOriginalSubset(List<ImportedCorner> imported,List<int[]> triangles,HashSet<int> membership,int[] sourceMap,Vector3 root,GroupReport report)
        {
            var selected=triangles.Where(t=>membership.Contains(t[0])&&membership.Contains(t[1])&&membership.Contains(t[2])).ToArray();
            report.originalSubsetTriangles=selected.Length;
            var used=new HashSet<int>(selected.SelectMany(t=>t));
            report.originalSubsetVertexCoverage=sourceMap.All(i=>used.Contains(i));
            report.originalSubsetReliable=selected.Length==report.outputTriangles&&report.originalSubsetVertexCoverage;
            if(!report.originalSubsetReliable)
            {
                report.originalSubsetNote="Original imported subset was not emitted: exact triangle count and mapped source-corner coverage did not both agree. Candidate retains extractor triangulation; no original-subset image claimed.";
                return null;
            }
            var p=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var tangents=new List<Vector4>();var indices=new List<int>();var remap=new Dictionary<int,int>();
            foreach(int[] face in selected)foreach(int sourceIndex in face)
            {
                if(!remap.TryGetValue(sourceIndex,out int target))
                {
                    ImportedCorner c=imported[sourceIndex];target=p.Count;remap.Add(sourceIndex,target);
                    p.Add(c.Position-root);n.Add(c.Normal);uv.Add(c.Uv);tangents.Add(c.Tangent);
                }
                indices.Add(target);
            }
            var mesh=new Mesh{name="PH01 original imported triangle subset "+report.id,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(p);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.SetTangents(tangents);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
            report.originalSubsetVertices=p.Count;
            report.originalSubsetNote="Actual imported leaf-slot triangles selected only when all corners belong to this mapped source group; exact triangle count and all mapped source-corner coverage agree. Native imported triangulation and tangent4 retained; source-root translation only.";
            return mesh;
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static bool Finite(Vector3 value){return Finite(value.x)&&Finite(value.y)&&Finite(value.z);}
        static string HierarchyPath(Transform t){return t.parent==null?t.name:HierarchyPath(t.parent)+"/"+t.name;}
        static Vector3 Tangent3(Vector4 t){return new Vector3(t.x,t.y,t.z);}
        static Vector3Int Cell(Vector3 p){return new Vector3Int(Mathf.FloorToInt(p.x/CellSize),Mathf.FloorToInt(p.y/CellSize),Mathf.FloorToInt(p.z/CellSize));}
        static void Require(bool ok,string message){if(!ok)throw new InvalidDataException("PH01 tuple candidate: "+message);}
        static void AddIssue(GroupReport report,int corner,string issue,Vector3 position,List<int> matches,List<ImportedCorner> imported)
        {
            if(report.firstIssues.Count>=12)return;
            report.firstIssues.Add(new Issue{corner=corner,issue=issue,position=position,importedCandidates=matches.Select(i=>"mesh"+imported[i].MeshId+"/vertex"+imported[i].Vertex).ToArray(),candidateTangents=matches.Select(i=>imported[i].Tangent).ToArray()});
        }
        struct ImportedCorner{public Vector3 Position,Normal;public Vector2 Uv;public Vector4 Tangent;public int MeshId,Vertex;}
        [Serializable] public sealed class Report
        {
            public string schema="citylife.ph01-imported-tuples-candidate.v1",sourceSha256,normalImportMode,tangentImportMode;
            public string status="Unaccepted controlled-comparison candidate; no runtime integration or appearance claim";
            public string scope="Actual imported P/N/UV/tangent4 tuples, transformed by source hierarchy; translation-only rosette pivot. No normal/UV flips, no new topology except optional exact tuple dedup, no tangent recalculation after copy.";
            public bool exactTupleDedup,mappingComplete,meshesCreated,numericChecksPassed;
            public int importedLeafVertices;public GroupReport[] groups;
        }
        [Serializable] public sealed class GroupReport
        {
            public string id;public Vector3 sourceRootUnity;
            public int inputCornerVertices,matchedCorners,unmatchedCorners,ambiguousCorners,outputVertices,outputTriangles,baselineTangentDirectionsChanged,baselineTangentHandednessChanged;
            public bool actualCornerTuplesPreserved,triangleCornerOrderPreserved,finiteUnitBasis,originalSubsetVertexCoverage,originalSubsetReliable;
            public int originalSubsetTriangles,originalSubsetVertices;public string originalSubsetNote;
            public float maximumPositionDelta,maximumUvDelta,minimumSourceNormalDot=1,minimumBaselineTangentDot=1;
            public List<Issue> firstIssues=new List<Issue>();
        }
        [Serializable] public sealed class Issue{public int corner;public string issue;public Vector3 position;public string[] importedCandidates;public Vector4[] candidateTangents;}
    }
}
#endif
