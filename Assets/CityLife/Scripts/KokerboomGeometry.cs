using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World
{
    [Serializable]
    public sealed class KokerboomDescriptor
    {
        public string generator = KokerboomGeometry.Version;
        public string componentBasis;
        public int seed, terminalRosettes, branchSegments, forkGenerations;
        public float age01, heightMetres, crownDiameterMetres, leafLengthMin, leafLengthMax;
        public Vector3 branchUnion, trunkBark, terminalRosette;
        public Vector3 lowerCrownOrigin, lowerCrownAxis;
    }

    /// <summary>Metre-scale Aloidendron dichotomum family. All morphology uses a private seed stream.
    /// Trunks/forks are one welded implicit surface. Default leaves are closed procedural
    /// volumes; configured inspection leaves retain their recorded source topology/maps.
    /// No billboards, UnityEngine.Random or world/save mutation are involved.</summary>
    public static class KokerboomGeometry
    {
        public const string Version = "citylife.aloidendron-dichotomum.v2-preview";
        struct Limb { public Vector3 a,b; public float ra,rb; public int path; }
        struct Rosette { public Vector3 p, axis; public float size, turn; }
        sealed class Form
        {
            public readonly List<Limb> limbs = new List<Limb>();
            public readonly List<Rosette> rosettes = new List<Rosette>();
            public KokerboomDescriptor description;
            public int nextPath=1;
        }
        sealed class Asset { public Mesh bark, leaves; public KokerboomDescriptor description; }
        sealed class PackedEdgeComparer : IEqualityComparer<ulong>
        {
            public static readonly PackedEdgeComparer Instance = new PackedEdgeComparer();
            public bool Equals(ulong x,ulong y) => x == y;
            public int GetHashCode(ulong key)
            {
                unchecked
                {
                    key ^= key >> 30; key *= 0xbf58476d1ce4e5b9UL;
                    key ^= key >> 27; key *= 0x94d049bb133111ebUL;
                    key ^= key >> 31;
                    return (int)(key ^ (key >> 32));
                }
            }
        }

        static readonly Dictionary<string,Asset> Cache = new Dictionary<string,Asset>();
        static Material barkMaterial, leafMaterial;
        static Mesh[] inspectionRosettes;
        static bool fittedCrownInspection;
        static float fittedStemRadius;
        static float Range(SeededRandom random,float lo,float hi) => Mathf.Lerp(lo,hi,(float)random.Next());

        public static KokerboomDescriptor Describe(int seed,float age01) => Skeleton(seed,age01).description;

        /// <summary>Unpublished editor experiment; source meshes remain caller-owned. No live world is changed.</summary>
        public static void ConfigureSourceRosettesForInspection(Mesh[] rosettes,Material leaves,Material wood)
        {
            if(Application.isPlaying||Cache.Count!=0)throw new InvalidOperationException("Configure source components before building an isolated inspection family.");
            if(rosettes==null||rosettes.Length!=5||leaves==null||wood==null)throw new ArgumentException("Five source rosettes and explicit mapped materials are required.");
            inspectionRosettes=rosettes;leafMaterial=leaves;barkMaterial=wood;fittedCrownInspection=false;
        }

        /// <summary>Separate unpublished PH02 comparison profile. The component origin
        /// must be its lower support centre, with +Y along its lower branch. Native crown
        /// footprint is retained; terminal wood matches the measured lower support radius.</summary>
        public static void ConfigureFittedCrownForInspection(Mesh crownAndSupport,Material surface,Material wood,float bottomRadiusMetres)
        {
            if(Application.isPlaying||Cache.Count!=0)throw new InvalidOperationException("Configure the isolated fitted-crown family before creating any meshes.");
            if(crownAndSupport==null||!crownAndSupport.isReadable||surface==null||wood==null||
                float.IsNaN(bottomRadiusMetres)||float.IsInfinity(bottomRadiusMetres)||bottomRadiusMetres<.04f||bottomRadiusMetres>.12f)
                throw new ArgumentException("A readable original-scale crown/support and measured 4–12 cm lower radius are required.");
            inspectionRosettes=new[]{crownAndSupport};leafMaterial=surface;barkMaterial=wood;
            fittedCrownInspection=true;fittedStemRadius=bottomRadiusMetres;
        }

        /// <summary>Detach caller-owned inspection assets only after all generated instances/cache are gone.</summary>
        public static void ResetSourceRosettesAfterInspection()
        {
            if(Application.isPlaying||Cache.Count!=0)throw new InvalidOperationException("Clear the isolated inspection cache before detaching source assets.");
            inspectionRosettes=null;leafMaterial=null;barkMaterial=null;fittedCrownInspection=false;fittedStemRadius=0;
        }

        /// <summary>Editor validation only. Call after destroying every instance using these meshes.</summary>
        public static void ClearCacheForValidation()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Live tree caches cannot be cleared during play.");
            foreach(var asset in Cache.Values){UnityEngine.Object.DestroyImmediate(asset.bark);UnityEngine.Object.DestroyImmediate(asset.leaves);}
            Cache.Clear();
        }

        public static GameObject Create(int seed,float age01,int lod=0)
        {
            if (float.IsNaN(age01) || float.IsInfinity(age01) || age01<0 || age01>1) throw new ArgumentOutOfRangeException(nameof(age01));
            if (lod<0 || lod>2) throw new ArgumentOutOfRangeException(nameof(lod));
            string key = seed+":"+BitConverter.SingleToInt32Bits(age01)+":"+lod;
            if (!Cache.TryGetValue(key,out Asset asset))
            {
                // Coarse authoring timings only. Start messages identify a still-running
                // or failed stage; these are build costs, not frame-rate measurements.
#if UNITY_EDITOR
                string timingKey="seed="+seed+" age="+age01.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" lod="+lod;
                var stageWatch=System.Diagnostics.Stopwatch.StartNew();
                Debug.Log("[Kokerboom stage] Skeleton START "+timingKey);
#endif
                Form form = Skeleton(seed,age01);
#if UNITY_EDITOR
                Debug.Log("[Kokerboom stage] Skeleton COMPLETE "+timingKey+" elapsedMs="+stageWatch.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                stageWatch.Restart();Debug.Log("[Kokerboom stage] Wood START "+timingKey+" limbs="+form.limbs.Count+" rosettes="+form.rosettes.Count);
#endif
                Mesh wood=Wood(form,lod);
#if UNITY_EDITOR
                Debug.Log("[Kokerboom stage] Wood COMPLETE "+timingKey+" elapsedMs="+stageWatch.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                stageWatch.Restart();Debug.Log("[Kokerboom stage] Leaves START "+timingKey);
#endif
                Mesh leaves=Leaves(form,seed,lod);
#if UNITY_EDITOR
                Debug.Log("[Kokerboom stage] Leaves COMPLETE "+timingKey+" elapsedMs="+stageWatch.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
#endif
                asset = new Asset { description=form.description, bark=wood, leaves=leaves };
                Cache.Add(key,asset);
            }
            EnsureMaterials();
            var root = new GameObject("Kokerboom "+seed+" age "+age01.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+" LOD"+lod);
            AddMesh(root,"Bark",asset.bark,barkMaterial);
            AddMesh(root,"Succulent rosettes",asset.leaves,leafMaterial);
            var inspection = new GameObject("Inspection"); inspection.transform.SetParent(root.transform,false);
            Marker(inspection,"BranchUnion",asset.description.branchUnion);
            Marker(inspection,"TrunkBark",asset.description.trunkBark);
            Marker(inspection,"TerminalRosette",asset.description.terminalRosette);
            Marker(inspection,"LowerCrownJoin",asset.description.lowerCrownOrigin);
            inspection.transform.Find("LowerCrownJoin").localRotation=Quaternion.FromToRotation(Vector3.up,asset.description.lowerCrownAxis);
            return root;
        }

        public static GameObject Spawn(int seed,float age01,Vector3 position,Quaternion rotation)
        {
            var root = new GameObject("Aloidendron dichotomum "+seed);
            root.transform.SetPositionAndRotation(position,rotation);
            var group = root.AddComponent<LODGroup>();
            var levels = new LOD[3];
            for(int i=0;i<3;i++)
            {
                var child=Create(seed,age01,i); child.transform.SetParent(root.transform,false);
                levels[i]=new LOD(i==0?.12f:i==1?.045f:.009f,child.GetComponentsInChildren<Renderer>());
            }
            group.SetLODs(levels); group.RecalculateBounds();
            return root;
        }

        /// <summary>The same generated terminal rosette, moved to the origin for botanical inspection.</summary>
        public static GameObject CreateRosette(int seed,int lod=0)
        {
            if(lod<0||lod>2)throw new ArgumentOutOfRangeException(nameof(lod));
            EnsureMaterials();string key="rosette:"+seed+":"+lod;
            if(!Cache.TryGetValue(key,out Asset asset))
            {
                Form form=Skeleton(seed,1f);Rosette rosette=form.rosettes[0];
                form.rosettes.Clear();rosette.p=Vector3.zero;rosette.axis=Vector3.up;form.rosettes.Add(rosette);
                asset=new Asset{leaves=Leaves(form,seed,lod),description=form.description};Cache.Add(key,asset);
            }
            var root=new GameObject("Isolated terminal rosette from seed "+seed);
            AddMesh(root,"Succulent rosette",asset.leaves,leafMaterial);return root;
        }

        static void Marker(GameObject parent,string name,Vector3 p)
        { var marker=new GameObject(name); marker.transform.SetParent(parent.transform,false); marker.transform.localPosition=p; }
        static void AddMesh(GameObject root,string name,Mesh mesh,Material material)
        {
            var child=new GameObject(name); child.transform.SetParent(root.transform,false);
            child.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=child.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true;
        }
        static void EnsureMaterials()
        {
            if(barkMaterial!=null && leafMaterial!=null)return;
            Shader shader=Shader.Find("CityLife/KokerboomSurface");
            if(shader==null)throw new InvalidOperationException("Kokerboom surface shader is missing.");
            barkMaterial=new Material(shader){name="Kokerboom · ochre peeling bark / pale powdery branches",enableInstancing=true};
            barkMaterial.SetFloat("_IsLeaf",0); barkMaterial.SetFloat("_Smoothness",.24f);
            leafMaterial=new Material(shader){name="Kokerboom · waxy blue-green succulent leaves",enableInstancing=true};
            leafMaterial.SetFloat("_IsLeaf",1); leafMaterial.SetFloat("_Smoothness",.34f);
        }

        static Form Skeleton(int seed,float age01)
        {
            if(float.IsNaN(age01)||float.IsInfinity(age01)||age01<0||age01>1)throw new ArgumentOutOfRangeException(nameof(age01));
            var r=new SeededRandom(seed); var f=new Form();
            float age=Mathf.Clamp01(age01), h=Mathf.Lerp(.45f,Range(r,5.8f,7.8f),Mathf.Pow(age,.83f));
            int generations=age<.10f?0:age<.25f?1:age<.46f?3:age<.78f?5:6;
            float forkFraction=Range(r,.22f,.37f);
            float trunkTop=generations==0?Mathf.Lerp(.025f,.10f,age/.10f):h*Mathf.Lerp(.56f,forkFraction,age);
            float radius=Mathf.Lerp(.035f,.59f,Mathf.Pow(age,1.05f))*Range(r,.86f,1.15f);
            if(generations==0)radius=Mathf.Lerp(.018f,.029f,age/.10f);
            Vector3 lean=new Vector3(Range(r,-.10f,.10f),0,Range(r,-.10f,.10f))*h;
            Vector3 fork=new Vector3(lean.x*.25f,trunkTop,lean.z*.25f);
            // Basal flare and subtle trunk curvature are part of the same connected volume.
            // Keep the basal endpoint sphere shallow enough for a planted root,
            // rather than burying a full trunk radius more than one metre deep.
            Vector3 previous=new Vector3(0,-.18f*radius-.06f,0); float previousRadius=radius*(generations==0?1.08f:1.02f);
            for(int s=1;s<=12;s++)
            {
                float t=s/12f;
                Vector3 p=new Vector3(lean.x*.25f*t*t,trunkTop*t,lean.z*.25f*t*t);
                float tipRadius=generations==0?.020f:radius*.58f;
                float rad=Mathf.Lerp(radius,tipRadius,t)+radius*(generations==0?.04f:.12f)*Mathf.Exp(-t*5f);
                f.limbs.Add(new Limb{a=previous,b=p,ra=previousRadius,rb=rad}); previous=p;previousRadius=rad;
            }
            // Short attached spreading root buttresses ground old trunks; not exposed tentacles.
            if(age>.28f)
            {
                // Union the basal roots as one field before the one trunk blend;
                // repeatedly smoothing six overlapping fields swelled a circular collar.
                int rootPath=f.nextPath++;
                for(int k=0;k<5;k++)
                {
                    float a=k*Mathf.PI*2/5+Range(r,-.23f,.23f);
                    Vector3 edge=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius*Range(r,1.18f,1.56f);
                    // A low buttress rises just outside the trunk, then tapers
                    // below soil. The old entire root ended below grade before
                    // emerging from the trunk, leaving a circular cut-off base.
                    Vector3 shoulder=edge*.68f+Vector3.up*radius*.045f;
                    f.limbs.Add(new Limb{a=new Vector3(0,-.05f*radius,0),b=shoulder,ra=radius*.34f,rb=radius*.18f,path=rootPath});
                    f.limbs.Add(new Limb{a=shoulder,b=edge+Vector3.down*radius*.065f,ra=radius*.18f,rb=radius*.03f,path=rootPath});
                }
            }
            // A broad, domed crown grows from curved primary forks. The target
            // envelope and its local branch clusters share the same seeded variation.
            float spread=h*Mathf.Lerp(.17f,.39f,age)*Range(r,.88f,1.17f);
            if(generations==0)
                // Plant the native-scale young crown closer to the soil. Its
                // lower support is underground; the leaf geometry is not resized.
                f.rosettes.Add(new Rosette{p=fittedCrownInspection?Vector3.down*.38f:fork,axis=Vector3.up,size=.98f+age,turn=Range(r,0,6.28f)});
            else
            {
                var tips=new List<Vector3>();
                // PH01's measured horizontal rosette footprint is about 0.45–0.60 m.
                // Fit terminal density to crown area, with close neighbouring leaves;
                // this is an art construction parameter, not a botanical growth law.
                float areaPerCrown=fittedCrownInspection?Range(r,.38f,.46f):Range(r,.135f,.175f);
                int count=generations>=5?Mathf.Clamp(Mathf.RoundToInt(Mathf.PI*spread*spread/areaPerCrown),fittedCrownInspection?16:40,fittedCrownInspection?80:190):1<<generations;
                float turn=Range(r,0,6.2832f);
                float depth=Range(r,.17f,.23f),ellipticity=Range(r,.80f,1.14f);
                for(int i=0;i<count;i++)
                {
                    float radial=Mathf.Sqrt((i+.55f)/count)*spread*Range(r,.94f,1.06f);
                    float angle=i*2.399963f+turn+Range(r,-.08f,.08f);
                    float y=h*(.94f-depth*Mathf.Pow(radial/spread,2))+Range(r,-.012f,.012f)*h;
                    y+=Mathf.Sin(angle*3+turn)*h*.018f;
                    tips.Add(new Vector3(Mathf.Cos(angle)*radial*ellipticity+lean.x*.32f,y,Mathf.Sin(angle)*radial+lean.z*.32f));
                }
                CrownBranches(f,r,fork,Vector3.up,previousRadius,tips,0,generations,turn,trunkTop);
            }
            float componentWidth=fittedCrownInspection?.45f:.30f,componentHeight=fittedCrownInspection?1.20f:.32f;
            float width=0,height=0; foreach(var p in f.rosettes){ width=Mathf.Max(width,new Vector2(p.p.x,p.p.z).magnitude+p.size*componentWidth);height=Mathf.Max(height,p.p.y+p.size*componentHeight); }
            Rosette focus=f.rosettes[0]; foreach(var p in f.rosettes)if(p.p.z<focus.p.z)focus=p;
            f.description=new KokerboomDescriptor{componentBasis=fittedCrownInspection?"PH02 fitted crown comparison":inspectionRosettes!=null?"PH01 extracted rosettes":"procedural leaves",seed=seed,age01=age,heightMetres=height,crownDiameterMetres=width*2,terminalRosettes=f.rosettes.Count,branchSegments=f.limbs.Count,forkGenerations=generations,leafLengthMin=.20f,leafLengthMax=.39f,branchUnion=fork+Vector3.up*.10f,trunkBark=new Vector3(lean.x*.05f,trunkTop*.38f,-radius*.9f),terminalRosette=focus.p+focus.axis*(fittedCrownInspection?.65f:.055f),lowerCrownOrigin=focus.p,lowerCrownAxis=focus.axis};
            return f;
        }

        static void CrownBranches(Form f,SeededRandom r,Vector3 from,Vector3 incoming,float radius,List<Vector3> tips,int level,int generations,float azimuth,float trunkHeight)
        {
            Vector3 split=new Vector3(Mathf.Cos(azimuth),0,Mathf.Sin(azimuth));
            tips.Sort((a,b)=>Vector3.Dot(a,split).CompareTo(Vector3.Dot(b,split)));
            float share=tips.Count>8?Range(r,.32f,.68f):Range(r,.42f,.58f);
            int partition=Mathf.Clamp(Mathf.RoundToInt(tips.Count*share),1,tips.Count-1);
            for(int side=0;side<2;side++)
            {
                var group=tips.GetRange(side==0?0:partition,side==0?partition:tips.Count-partition);
                Vector3 centroid=Vector3.zero;foreach(var tip in group)centroid+=tip;centroid/=group.Count;
                bool terminal=group.Count==1;
                Vector3 end=centroid;
                if(!terminal)
                {
                    float lowestTip=float.PositiveInfinity;foreach(var tip in group)lowestTip=Mathf.Min(lowestTip,tip.y);
                    float extent=0;foreach(var tip in group)extent=Mathf.Max(extent,new Vector2(tip.x-centroid.x,tip.z-centroid.z).magnitude);
                    // Locate each junction from this parent's remaining cluster, rather
                    // than a shared global height. Early arms travel outward before rising;
                    // later orders shorten as their supported patch of foliage shrinks.
                    float horizontal=level==0?Range(r,.92f,1.06f):Range(r,.80f,1.02f);
                    end.x=Mathf.LerpUnclamped(from.x,centroid.x,horizontal);
                    end.z=Mathf.LerpUnclamped(from.z,centroid.z,horizontal);
                    float rise=level==0?Range(r,.24f,.47f):level==1?Range(r,.35f,.61f):Range(r,.43f,.71f);
                    float clearance=Mathf.Max(.09f,extent*Range(r,.20f,.32f));
                    end.y=Mathf.Min(Mathf.Lerp(from.y,centroid.y,rise),lowestTip-clearance);
                    end.y=Mathf.Max(from.y+.035f,end.y);
                }
                Vector3 direction=(end-from).normalized;float branchLength=(end-from).magnitude;
                Vector3 tangent0=(incoming*.58f+direction*.42f).normalized;
                Vector3 tangent1=(direction*.68f+Vector3.up*.32f).normalized;
                Vector3 sway=Vector3.Cross(direction,Vector3.up)*branchLength*Range(r,-.14f,.14f);
                Vector3 control0=from+tangent0*branchLength*.38f+sway;
                Vector3 control1=end-tangent1*branchLength*.34f-sway*.35f;
                float supported=group.Count/(float)tips.Count;
                float startRadius=Mathf.Max(.021f,radius*Mathf.Sqrt(supported)*Range(r,1.00f,1.10f));
                float endRadius=terminal?Range(r,.021f,.027f):Mathf.Max(.026f,startRadius*Range(r,.76f,.87f));
                float crownScale=Range(r,.96f,1.10f);
                if(fittedCrownInspection)
                {
                    endRadius=Mathf.Max(endRadius,terminal?fittedStemRadius*crownScale:fittedStemRadius*Mathf.Sqrt(group.Count)*.78f);
                    startRadius=Mathf.Max(startRadius,endRadius*1.045f);
                }
                Vector3 prev=from;float prevRadius=startRadius;int path=f.nextPath++;
                // Sample long curved limbs more finely so the implicit surface
                // does not inherit seven visibly straight sections at every scale.
                int curveSegments=Mathf.Clamp(Mathf.CeilToInt(branchLength/.13f),7,28);
                for(int j=1;j<=curveSegments;j++)
                {
                    float t=j/(float)curveSegments,u=1-t; Vector3 p=u*u*u*from+3*u*u*t*control0+3*u*t*t*control1+t*t*t*end;
                    float rad=Mathf.Lerp(startRadius,endRadius,t);
                    f.limbs.Add(new Limb{a=prev,b=p,ra=prevRadius,rb=rad,path=path}); prev=p;prevRadius=rad;
                }
                if(terminal)
                {
                    Vector3 outward=new Vector3(end.x,0,end.z).normalized;
                    Vector3 axis=(Vector3.up+outward*Range(r,.08f,.36f)+tangent1*.10f).normalized;
                    // The fitted support starts at the terminal endpoint. Its flat
                    // lower radial tangent contains the wood cap; sinking it eight
                    // centimetres into the narrowing shaft made two visible rings.
                    // PH01 retains its earlier short leaf-root overlap.
                    if(fittedCrownInspection)axis=tangent1;
                    f.rosettes.Add(new Rosette{p=fittedCrownInspection?end:end-axis*.018f,axis=axis,size=crownScale,turn=Range(r,0,6.2832f)});
                }
                else CrownBranches(f,r,end,tangent1,endRadius,group,level+1,generations,azimuth+1.5708f+Range(r,-.62f,.62f),trunkHeight);
            }
        }

        sealed class MeshData
        {
            public readonly List<Vector3> points=new List<Vector3>(), normals=new List<Vector3>();
            public readonly List<Color> colors=new List<Color>(); public readonly List<Vector2> uv=new List<Vector2>();
            public readonly List<int> triangles=new List<int>();
            public Mesh Finish(string name)
            { var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.SetVertices(points);m.SetNormals(normals);m.SetColors(colors);m.SetUVs(0,uv);m.SetTriangles(triangles,0);m.RecalculateBounds();return m; }
        }

        static Mesh Wood(Form f,int lod)
        {
            // Rasterize tapered capsule distances locally, then polygonize the single union.
            // Sparse segment bounds avoid evaluating every branch at every lattice point.
            Vector3 min=Vector3.one*999,max=Vector3.one*-999;
            foreach(var s in f.limbs){float pad=Mathf.Max(s.ra,s.rb)+.16f;min=Vector3.Min(min,Vector3.Min(s.a,s.b)-Vector3.one*pad);max=Vector3.Max(max,Vector3.Max(s.a,s.b)+Vector3.one*pad);}
            float step=(lod==0?.018f:lod==1?.040f:.075f)*Mathf.Clamp(f.description.heightMetres/5,.48f,1f);
            int nx=Mathf.CeilToInt((max.x-min.x)/step)+1,ny=Mathf.CeilToInt((max.y-min.y)/step)+1,nz=Mathf.CeilToInt((max.z-min.z)/step)+1;
            int stride=nx*ny;float[] field=new float[stride*nz];for(int i=0;i<field.Length;i++)field[i]=10f;
            // Each continuous branch path is unioned once. Blending every short capsule
            // separately inflated every sample join and produced the observed corrugation.
            var pathFields=new Dictionary<int,float>();int currentPath=-1;float pathSmooth=0;
            foreach(var s in f.limbs)
            {
                if(s.path!=currentPath){FlushPath();currentPath=s.path;}
                float ra=Mathf.Max(s.ra,step*.85f),rb=Mathf.Max(s.rb,step*.85f);
                float smooth=Mathf.Clamp(Mathf.Max(ra,rb)*.36f,.018f,.17f);
                pathSmooth=Mathf.Max(pathSmooth,smooth);
                float pad=Mathf.Max(ra,rb)+smooth+step*2;
                Vector3 lo=(Vector3.Min(s.a,s.b)-Vector3.one*pad-min)/step,hi=(Vector3.Max(s.a,s.b)+Vector3.one*pad-min)/step;
                int x0=Mathf.Max(0,Mathf.FloorToInt(lo.x)),x1=Mathf.Min(nx-1,Mathf.CeilToInt(hi.x));
                int y0=Mathf.Max(0,Mathf.FloorToInt(lo.y)),y1=Mathf.Min(ny-1,Mathf.CeilToInt(hi.y));
                int z0=Mathf.Max(0,Mathf.FloorToInt(lo.z)),z1=Mathf.Min(nz-1,Mathf.CeilToInt(hi.z));
                Vector3 ab=s.b-s.a;float length=ab.magnitude;
                Vector3 axis=ab/Mathf.Max(length,1e-8f);
                float radiusDelta=rb-ra;
                bool contained=Mathf.Abs(radiusDelta)>=length;
                float slope=contained?0:radiusDelta/length;
                float radialFactor=Mathf.Sqrt(Mathf.Max(0,1-slope*slope));
                for(int z=z0;z<=z1;z++)for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
                {
                    Vector3 p=min+new Vector3(x,y,z)*step;
                    Vector3 relative=p-s.a;float along=Vector3.Dot(relative,axis);
                    float radial=Mathf.Sqrt(Mathf.Max(0,relative.sqrMagnitude-along*along));
                    float d;
                    // Exact envelope of spheres whose centres and radii vary linearly.
                    // Axis-nearest projection followed by radius interpolation creates
                    // a derivative seam at each taper segment, visible as trunk rings.
                    // The tangent plane meets each endpoint sphere at the same normal.
                    if(contained)d=(p-(ra>=rb?s.a:s.b)).magnitude-Mathf.Max(ra,rb);
                    else
                    {
                        float tangentAlong=along*radialFactor+radial*slope;
                        if(tangentAlong<0)d=relative.magnitude-ra;
                        else if(tangentAlong>length*radialFactor)d=(p-s.b).magnitude-rb;
                        else d=radial*radialFactor-along*slope-ra;
                    }
                    int index=x+y*nx+z*stride;
                    if(!pathFields.TryGetValue(index,out float oldDistance)||d<oldDistance)pathFields[index]=d;
                }
            }
            FlushPath();
            var output=new MeshData();var edgeCache=new Dictionary<ulong,int>(PackedEdgeComparer.Instance);
            int[] corners={0,1,1+nx,nx,stride,1+stride,1+nx+stride,nx+stride};
            int[,] tets={{0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6}};
            int[] ids=new int[4],inside=new int[4],outside=new int[4];
            Vector3 tetraOutward=Vector3.zero;
            for(int z=0;z<nz-1;z++)for(int y=0;y<ny-1;y++)for(int x=0;x<nx-1;x++)
            {
                int cell=x+y*nx+z*stride;bool neg=false,pos=false;for(int k=0;k<8;k++){if(field[cell+corners[k]]<0)neg=true;else pos=true;}if(!neg||!pos)continue;
                for(int t=0;t<6;t++)
                {
                    int ni=0,no=0;for(int k=0;k<4;k++){ids[k]=cell+corners[tets[t,k]];if(field[ids[k]]<0)inside[ni++]=ids[k];else outside[no++]=ids[k];}
                    if(ni==0||ni==4)continue;
                    // Inside/outside classification supplies a consistent surface orientation.
                    // Interpolated shading gradients can disagree near saddle points at forks.
                    Vector3 inner=Vector3.zero,outer=Vector3.zero;
                    for(int k=0;k<ni;k++)inner+=Position(inside[k]);
                    for(int k=0;k<no;k++)outer+=Position(outside[k]);
                    tetraOutward=outer/no-inner/ni;
                    if(ni==1||ni==3)
                    {
                        int lone=ni==1?inside[0]:outside[0];int[] other=ni==1?outside:inside;
                        int a=Edge(lone,other[0]),b=Edge(lone,other[1]),c=Edge(lone,other[2]);Face(a,b,c);
                    }
                    else
                    { int a=Edge(inside[0],outside[0]),b=Edge(inside[0],outside[1]),c=Edge(inside[1],outside[0]),d=Edge(inside[1],outside[1]);Face(a,b,c);Face(b,d,c); }
                }
            }
            return output.Finish("Kokerboom continuous wood LOD"+lod+" seed "+f.description.seed);

            void FlushPath()
            {
                foreach(var sample in pathFields)
                {
                    float a=field[sample.Key],d=sample.Value;
                    float blend=Mathf.Max(pathSmooth-Mathf.Abs(a-d),0)/Mathf.Max(pathSmooth,1e-6f);
                    field[sample.Key]=Mathf.Min(a,d)-blend*blend*pathSmooth*.25f;
                }
                pathFields.Clear();pathSmooth=0;
            }

            Vector3 Position(int id){int z=id/stride,y=(id-z*stride)/nx,x=id%nx;return min+new Vector3(x,y,z)*step;}
            Vector3 Gradient(int id)
            { int z=id/stride,y=(id-z*stride)/nx,x=id%nx;return new Vector3(field[Mathf.Min(x+1,nx-1)+y*nx+z*stride]-field[Mathf.Max(x-1,0)+y*nx+z*stride],field[x+Mathf.Min(y+1,ny-1)*nx+z*stride]-field[x+Mathf.Max(y-1,0)*nx+z*stride],field[x+y*nx+Mathf.Min(z+1,nz-1)*stride]-field[x+y*nx+Mathf.Max(z-1,0)*stride]); }
            int Edge(int a,int b)
            {
                ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);if(edgeCache.TryGetValue(key,out int found))return found;
                // Keep iso intersections a sub-millimetre distance off lattice corners. This
                // prevents numerically collapsed sliver triangles while sharing the same edge vertex.
                float t=Mathf.Clamp(field[a]/(field[a]-field[b]),.005f,.995f);Vector3 p=Vector3.Lerp(Position(a),Position(b),t),n=Vector3.Lerp(Gradient(a),Gradient(b),t).normalized;
                int id=output.points.Count; output.points.Add(p);output.normals.Add(n);
                float trunk=Mathf.Clamp01((f.description.branchUnion.y+.45f-p.y)/.9f);
                trunk*=Mathf.SmoothStep(0,1,f.description.age01/.32f);
                uint pattern=unchecked((uint)f.description.seed*747796405u+2891336453u);
                float sourceOffset=(pattern&0x00ffffffu)/16777215f;
                output.colors.Add(new Color(trunk,Mathf.Clamp01(f.description.age01/.3f),sourceOffset,1));output.uv.Add(new Vector2(p.y,Mathf.Atan2(p.z,p.x)));edgeCache.Add(key,id);return id;
            }
            void Face(int a,int b,int c)
            { if(Vector3.Dot(Vector3.Cross(output.points[b]-output.points[a],output.points[c]-output.points[a]),tetraOutward)<0){int swap=b;b=c;c=swap;}output.triangles.Add(a);output.triangles.Add(b);output.triangles.Add(c); }
        }

        static Mesh Leaves(Form f,int seed,int lod)
        {
            if(inspectionRosettes!=null)return SourceLeaves(f,seed,lod);
            var m=new MeshData();var r=new SeededRandom(unchecked(seed+981723));
            float measuredMin=float.MaxValue,measuredMax=0;
            foreach(var rosette in f.rosettes)
            {
                Quaternion orient=Quaternion.FromToRotation(Vector3.up,rosette.axis);
                // Spiralled overlapping ranks: older outer leaves spread, young inner blades erect.
                bool juvenile=f.description.forkGenerations==0;
                int count=juvenile?(lod==0?18:lod==1?14:10):(lod==0?38:lod==1?26:15);
                for(int i=0;i<count;i++)
                {
                    float rank=i/(float)(count-1),az=i*2.399963f+rosette.turn+Range(r,-.11f,.11f);
                    if(juvenile)az=rosette.turn+(i%5)*Mathf.PI*2/5+Range(r,-.10f,.10f);
                    float length=Mathf.Lerp(.35f,.20f,rank)*rosette.size*Range(r,.89f,1.08f);
                    measuredMin=Mathf.Min(measuredMin,length);measuredMax=Mathf.Max(measuredMax,length);
                    float width=length*Range(r,.073f,.096f);
                    float angle=Mathf.Lerp(1.52f,.10f,Mathf.Pow(rank,.75f))+Range(r,-.08f,.08f);
                    Leaf(m,rosette.p,orient,az,angle,length,width,rank,lod,Range(r,.85f,1.1f));
                }
            }
            f.description.leafLengthMin=measuredMin;f.description.leafLengthMax=measuredMax;
            return m.Finish("Kokerboom closed succulent rosettes LOD"+lod+" seed "+seed);
        }

        static Mesh SourceLeaves(Form f,int seed,int lod)
        {
            var m=new MeshData();var r=new SeededRandom(unchecked(seed+981723));
            var copiedTangents=fittedCrownInspection?new List<Vector4>():null;
            var copiedBranchUvs=fittedCrownInspection?new List<Vector2>():null;
            foreach(var rosette in f.rosettes)
            {
                Mesh source=inspectionRosettes[(int)(r.Next()*inspectionRosettes.Length)%inspectionRosettes.Length];
                Vector3[] points=source.vertices,normals=source.normals;Vector2[] uv=source.uv;int[] triangles=source.triangles;
                Vector4[] sourceTangents=fittedCrownInspection?source.tangents:null;
                Color[] sourceColors=fittedCrownInspection?source.colors:null;
                Vector2[] branchUvs=fittedCrownInspection?source.uv2:null;
                if(fittedCrownInspection&&(sourceTangents.Length!=points.Length||sourceColors.Length!=points.Length||branchUvs.Length!=points.Length))
                    throw new InvalidOperationException("Fitted crown tangents and source/support blend weights must be complete.");
                Quaternion rotation=Quaternion.FromToRotation(Vector3.up,rosette.axis)*Quaternion.Euler(0,rosette.turn*Mathf.Rad2Deg,0);
                int offset=m.points.Count;
                for(int i=0;i<points.Length;i++)
                {
                    m.points.Add(rosette.p+rotation*(points[i]*rosette.size));m.normals.Add(rotation*normals[i]);
                    m.uv.Add(uv[i]);m.colors.Add(fittedCrownInspection?sourceColors[i]:Color.white);
                    if(fittedCrownInspection)
                    {
                        Vector4 sourceTangent=sourceTangents[i];Vector3 rotated=rotation*new Vector3(sourceTangent.x,sourceTangent.y,sourceTangent.z);
                        copiedTangents.Add(new Vector4(rotated.x,rotated.y,rotated.z,sourceTangent.w));
                        copiedBranchUvs.Add(branchUvs[i]);
                    }
                }
                foreach(int index in triangles)m.triangles.Add(offset+index);
            }
            Mesh mesh=m.Finish((fittedCrownInspection?"Hybrid PH02 fitted crowns":"Hybrid PH01 source rosettes")+" - experimental LOD"+lod+" seed "+seed);
            if(fittedCrownInspection){mesh.SetTangents(copiedTangents);mesh.SetUVs(1,copiedBranchUvs);}else mesh.RecalculateTangents();return mesh;
        }

        static void Leaf(MeshData m,Vector3 centre,Quaternion orient,float az,float angle,float length,float width,float rank,int lod,float colour)
        {
            int rings=lod==0?9:lod==1?6:4,sides=lod==0?8:6;int begin=m.points.Count;
            Vector3 radial=new Vector3(Mathf.Cos(az),0,Mathf.Sin(az)),across=new Vector3(-radial.z,0,radial.x);
            Vector3 baseOffset=radial*.008f+Vector3.up*(rank*.025f-.035f);
            Vector3 cp1=baseOffset+Vector3.up*length*.30f;
            Vector3 cp2=baseOffset+radial*(length*.65f*Mathf.Sin(angle))+Vector3.up*(length*(.65f*Mathf.Cos(angle*.7f)+.05f));
            Vector3 cp3=baseOffset+radial*(length*Mathf.Sin(angle))+Vector3.up*(length*(Mathf.Cos(angle)-.20f*(1-rank)*(1-rank)));
            for(int j=0;j<rings;j++)
            {
                float t=j/(float)rings;
                float taper=Mathf.Pow(Mathf.Sin(Mathf.PI*Mathf.Lerp(.12f,1f,t)),.9f);
                float u=1-t;
                Vector3 p=u*u*u*baseOffset+3*u*u*t*cp1+3*u*t*t*cp2+t*t*t*cp3;
                Vector3 tangent=(3*u*u*(cp1-baseOffset)+6*u*t*(cp2-cp1)+3*t*t*(cp3-cp2)).normalized;
                Vector3 top=Vector3.Cross(across,tangent).normalized;
                float halfWidth=width*taper,thick=width*.29f*taper;
                for(int k=0;k<sides;k++)
                {
                    float phase=k*Mathf.PI*2/sides,c=Mathf.Cos(phase),s=Mathf.Sin(phase);
                    Vector3 normal=(across*c+top*s*2.3f).normalized;
                    Vector3 v=centre+orient*(p+across*c*halfWidth+top*s*thick);
                    m.points.Add(v);m.normals.Add(orient*normal);
                    float wax=Mathf.Lerp(.77f,1.12f,t)*colour; m.colors.Add(new Color(.27f*wax,.48f*wax,.43f*wax,1));m.uv.Add(new Vector2(t,phase/6.2832f));
                    if(j<rings-1){int a=begin+j*sides+k,b=begin+j*sides+(k+1)%sides,c0=a+sides,d=b+sides;m.triangles.Add(a);m.triangles.Add(c0);m.triangles.Add(b);m.triangles.Add(b);m.triangles.Add(c0);m.triangles.Add(d);}
                }
            }
            for(int k=1;k<sides-1;k++){m.triangles.Add(begin);m.triangles.Add(begin+k+1);m.triangles.Add(begin+k);}
            // A single true pointed apex avoids a microscopic flat cap and its sliver faces.
            int tip=m.points.Count,lastRing=begin+(rings-1)*sides;
            m.points.Add(centre+orient*cp3);m.normals.Add(orient*(cp3-cp2).normalized);
            m.colors.Add(new Color(.27f*1.12f*colour,.48f*1.12f*colour,.43f*1.12f*colour,1));m.uv.Add(new Vector2(1,.5f));
            for(int k=0;k<sides;k++){m.triangles.Add(lastRing+k);m.triangles.Add(tip);m.triangles.Add(lastRing+(k+1)%sides);}
            // Small marginal teeth are millimetre-scale succulent details, omitted at distant LOD.
            if(lod==0)for(int side=-1;side<=1;side+=2)for(int j=1;j<=5;j++)
            {
                float t=.12f+j*.13f,u=1-t;
                float taper=Mathf.Pow(Mathf.Sin(Mathf.PI*Mathf.Lerp(.12f,1f,t)),.9f);
                Vector3 p=u*u*u*baseOffset+3*u*u*t*cp1+3*u*t*t*cp2+t*t*t*cp3;
                Vector3 tangent=(3*u*u*(cp1-baseOffset)+6*u*t*(cp2-cp1)+3*t*t*(cp3-cp2)).normalized;
                Vector3 top=Vector3.Cross(across,tangent).normalized;
                Vector3 edge=p+across*(side*width*taper*.985f);
                float tooth=length*.010f;
                Vector3 a=centre+orient*(edge-tangent*tooth),b=centre+orient*(edge+tangent*tooth),c=centre+orient*(edge+top*tooth*.6f),d=centre+orient*(edge+across*(side*tooth*1.2f));
                Tooth(a,c,b);Tooth(a,b,d);Tooth(b,c,d);Tooth(c,a,d);
            }
            void Tooth(Vector3 a,Vector3 b,Vector3 c)
            {
                int id=m.points.Count;Vector3 cross=Vector3.Cross(b-a,c-a);
                // Unity's general-purpose normalization zeros very small vectors; these
                // millimetre-scale marginal teeth still need a unit shading normal.
                Vector3 normal=cross/Mathf.Sqrt(Mathf.Max(cross.sqrMagnitude,1e-30f));
                m.points.Add(a);m.points.Add(b);m.points.Add(c);
                for(int i=0;i<3;i++){m.normals.Add(normal);m.colors.Add(new Color(.53f,.61f,.42f));m.uv.Add(Vector2.zero);m.triangles.Add(id+i);}
            }
        }
    }
}
