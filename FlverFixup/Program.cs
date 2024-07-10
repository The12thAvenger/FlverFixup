using System.CommandLine;
using System.CommandLine.Parsing;
using System.Numerics;
using SoulsFormats;

namespace FlverFixup;

public static class Program
{
    public static void Main(string[] args)
    {
        RootCommand rootCommand = new("Flver Fixup Tool" +
                                      "\nFixes common issues with FLVER2 model files." +
                                      "\nSome of these fixes are situational, only apply them if you're sure you need to.");
        
        Argument<string> inputPathArg = new("input", "Path to the input flver, BND4 or folder. " +
                                                     "\nIf a bnd is provided, all flver files in the bnd will be processed." +
                                                     "\nIf a folder is provided, all flvers, bnds and subfolders will be processed.");

        Option<string> outputPathOption = new(["-o", "--output"], "Overrides the output path, which is equal to the input path by default." +
                                                                  "\nIf a folder is provided as input this is used as an output folder to which all processed files and subfolders will be written.");
        Option<List<int>> fixFaceWindingOption = new(["-f", "--face"], ParseIntListOption, false,
            "Fixes face winding to match the vertex normals for meshes with the supplied indices or all meshes if no indices are given" +
            "\nUse this option if shadows appear on the wrong side of the mesh faces.")
            {
                Arity = ArgumentArity.ZeroOrMore,
                AllowMultipleArgumentsPerToken = true
            };
        Option<List<int>> fixLodsOption = new(["-l", "--lod"], ParseIntListOption, false,
            "Adds LOD and motion blur facesets to meshes with the supplied indices or all meshes if no indices are given." +
            "\nUse this option if meshes disappear when far from the camera.")
            {
                Arity = ArgumentArity.ZeroOrMore,
                AllowMultipleArgumentsPerToken = true
            };
        Option<List<int>> fixDecalsOption = new(["-d", "--decal"], ParseIntListOption, false,
            "Removes decal uvs from meshes with the supplied indices or all meshes if no indices are given." +
            "\nThis option assumes that the decal uvs are stored in the second uv channel. " +
            "\nUse this option to completely remove blood effects or other decals from being applied to your model.")
            {
                Arity = ArgumentArity.ZeroOrMore,
                AllowMultipleArgumentsPerToken = true
            };
        Option<bool> removeEmptyMeshesOption = new(["-r", "--remove"], 
            "Removes meshes with no vertices or no facesets." +
            "\nCan be useful if 3d modeler applications have difficulty importing your model or to reduce file size.");

        Option<bool> fixNodesOption = new(["-n", "--node"], 
            "Makes sure all nodes are included in the skeleton definitions and that all node references are valid and sets the node flags appropriately." +
            "\nUse this option if you are getting crashes with old custom flvers in Elden Ring version 1.12 or newer.");

        Option<bool> permissiveOption = new(["-p", "--permissive"],
            "Disables asserts when loading files. Can be required for backwards compatibility with older tooling." +
            "\nUse this option if you are getting assertion errors when loading your flver.");
        
        rootCommand.AddArgument(inputPathArg);
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(fixFaceWindingOption);
        rootCommand.AddOption(fixLodsOption);
        rootCommand.AddOption(fixDecalsOption);
        rootCommand.AddOption(removeEmptyMeshesOption);
        rootCommand.AddOption(fixNodesOption);
        rootCommand.AddOption(permissiveOption);
        
        rootCommand.SetHandler(Run, inputPathArg, outputPathOption, fixFaceWindingOption, fixLodsOption, fixDecalsOption, removeEmptyMeshesOption, fixNodesOption, permissiveOption);
        rootCommand.Invoke(args);
        
        if (args.Length == 0)
        {
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    //can't default to null so we create a list with -1 to distinguish between option specified with no arguments or not specified
    private static List<int> ParseIntListOption(ArgumentResult result)
    {
        return result.Tokens.Count > 0 ? result.Tokens.Select(x => int.Parse(x.Value)).ToList() : [-1];
    }
    
    private static void Run(string input, string? output, List<int> fixFaceWinding, List<int> fixLods, List<int> fixDecals, bool removeEmptyMeshes, bool fixNodes, bool permissive)
    {
        BinaryReaderEx.IsFlexible = permissive;
        
        output ??= input;
        if (Directory.Exists(input))
        {
            foreach (string entry in Directory.GetFileSystemEntries(input))
            {
                if (Path.GetExtension(entry) == ".bak") continue;
                string relPath = Path.GetRelativePath(input, entry);
                string inputPath = Path.Join(input, relPath);
                string outputPath = Path.Join(output, relPath);
                Run(inputPath, outputPath, fixFaceWinding, fixLods, fixDecals, removeEmptyMeshes, fixNodes, permissive);
            }
            return;
        }

        if (!File.Exists(input))
        {
            Console.WriteLine("The provided file path does not exist.");
            return;
        }

        if (BND4.Is(input))
        {
            Console.WriteLine($"Processing \"{input}\"");
            BND4 bnd;
            try
            {
                bnd = BND4.Read(input);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to read BND4 at path \"{input}\"");
                Console.WriteLine(e);
                return;
            }

            bool hasChanged = false;
            foreach (BinderFile binderFile in bnd.Files.Where(x => Path.GetExtension(x.Name).ToLower() == ".flver"))
            {
                if (!FLVER2.Is(binderFile.Bytes)) continue;
                Console.WriteLine($"Processing \"{binderFile.Name}\"");

                FLVER2 flver;
                try
                {
                    flver = FLVER2.Read(binderFile.Bytes);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to read FLVER2 from BND4.");
                    Console.WriteLine(e);
                    continue;
                }

                hasChanged |= ProcessFlver(flver, fixFaceWinding, fixLods, fixDecals, removeEmptyMeshes, fixNodes);

                try
                {
                    binderFile.Bytes = flver.Write();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to write FLVER2 to BND4");
                    Console.WriteLine(e);
                }
            }

            try
            {
                if (hasChanged)
                {
                    Console.WriteLine($"Writing changes to path \"{output}\"");
                    bnd.Write(output);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to write BND4 to path \"{output}\"");
                Console.WriteLine(e);
                throw;
            }
        }
        else if (FLVER2.Is(input))
        {
            Console.WriteLine($"Processing \"{input}\"");
            FLVER2 flver;
            try
            {
                flver = FLVER2.Read(input);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to read FLVER2 from path \"{input}\".");
                Console.WriteLine(e);
                return;
            }
            bool hasChanged = ProcessFlver(flver, fixFaceWinding, fixLods, fixDecals, removeEmptyMeshes, fixNodes);

            try
            {
                if (hasChanged)
                {
                    Console.WriteLine($"Writing changes to path \"{output}\"");
                    flver.Write(output);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to write FLVER2 to path \"{output}\".");
                Console.WriteLine(e);
            }
        }
    }
    
    private static bool ProcessFlver(FLVER2 flver, List<int> fixFaceWinding, List<int> fixLods,
        List<int> fixDecals, bool removeEmptyMeshes, bool fixNodes)
    {
        try
        {
            bool hasChanged = false;
            if (fixFaceWinding.Count > 0) hasChanged |= FixFaceWinding(flver, fixFaceWinding);
            if (fixLods.Count > 0) hasChanged |= FixLods(flver, fixLods);
            if (fixDecals.Count > 0) hasChanged |= FixDecals(flver, fixDecals);
            if (removeEmptyMeshes) hasChanged |= RemoveEmptyMeshes(flver);
            if (fixNodes) hasChanged |= FixNodes(flver);
            return hasChanged;
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to process flver.");
            Console.WriteLine(e);
            return false;
        }
    }

    private static bool RemapNodeIndex(List<FLVER.Node> oldList, List<FLVER.Node> newList, HashSet<int> remappedIndices, int index, Action<short> setIndex)
    {
        if (index == -1)
        {
            return false;
        }

        if (index < 0 || index >= oldList.Count)
        {
            Console.WriteLine($"Invalid node index {index} found, mapping to -1.");
        }
        
        FLVER.Node node = oldList[index];
        int newIndex = newList.IndexOf(node);
        if (newIndex == index) return false;

        setIndex((short)newIndex);
        if (node.Flags != FLVER.Node.NodeFlags.Disabled && !remappedIndices.Contains(index))
        {
            remappedIndices.Add(index);
            Console.WriteLine($"Remapping node index {index} to index {newIndex}.");
        }
        return true;
    }

    private static bool SetNodeFlag(FLVER.Node node, FLVER.Node.NodeFlags flag)
    {
        if (node.Flags.HasFlag(flag)) return false;
        
        node.Flags |= flag;
        node.Flags &= flag == FLVER.Node.NodeFlags.Disabled ? FLVER.Node.NodeFlags.Disabled : ~FLVER.Node.NodeFlags.Disabled;
        Console.WriteLine($"Setting {flag.ToString()} flag on node \"{node.Name}\"");
        return true;
    }
    
    private static bool FixNodes(FLVER2 flver)
    {
        bool hasChanged = false;

        List<(int index, Action<short> setIndex)> callbacks = [];
        for (int i = 0; i < flver.Meshes.Count; i++)
        {
            FLVER2.Mesh mesh = flver.Meshes[i];
            foreach (FLVER.Vertex vertex in mesh.Vertices)
            {
                if (mesh.UseBoneWeights)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        int boneIndex = vertex.BoneIndices[j];
                        if (vertex.BoneWeights[j] == 0 || boneIndex < 0 || boneIndex >= flver.Nodes.Count) continue;
                        FLVER.Node node = flver.Nodes[boneIndex];
                        int weightIndex = j;
                        hasChanged |= SetNodeFlag(node, FLVER.Node.NodeFlags.Bone);
                        callbacks.Add((boneIndex, x => vertex.BoneIndices[weightIndex] = x));
                    }
                }
                else if (vertex.NormalW >= 0 && vertex.NormalW < flver.Nodes.Count)
                {
                    int normalW = vertex.NormalW;
                    FLVER.Node node = flver.Nodes[normalW];
                    hasChanged |= SetNodeFlag(node, FLVER.Node.NodeFlags.Bone);
                    callbacks.Add((normalW, x => vertex.NormalW = x));
                }
            }

            int meshNodeIndex = mesh.NodeIndex;
            if (meshNodeIndex == -1) continue;
            if (meshNodeIndex < 0 || meshNodeIndex >= flver.Nodes.Count)
            {
                hasChanged = true;
                Console.WriteLine($"Invalid node index {meshNodeIndex} in mesh at index {i} with material {flver.Materials[mesh.MaterialIndex]}, setting to -1.");
                mesh.NodeIndex = -1;
                continue;
            }

            FLVER.Node meshNode = flver.Nodes[meshNodeIndex];
            hasChanged |= SetNodeFlag(meshNode, FLVER.Node.NodeFlags.Mesh);
            callbacks.Add((meshNodeIndex, x => mesh.NodeIndex = x));
        }

        for (int i = 0; i < flver.Dummies.Count; i++)
        {
            FLVER.Dummy dummy = flver.Dummies[i];
            if (dummy.AttachBoneIndex != -1)
            {
                if (dummy.AttachBoneIndex < 0 || dummy.AttachBoneIndex >= flver.Nodes.Count)
                {
                    hasChanged = true;
                    Console.WriteLine($"Invalid attached node index {dummy.AttachBoneIndex} in dummy poly at index {i} with refId {dummy.ReferenceID}, setting to -1.");
                    dummy.AttachBoneIndex = -1;
                }
                else
                {
                    FLVER.Node node = flver.Nodes[dummy.AttachBoneIndex];
                    hasChanged |= SetNodeFlag(node, FLVER.Node.NodeFlags.DummyOwner);
                    callbacks.Add((dummy.AttachBoneIndex, x => dummy.AttachBoneIndex = x));
                }
            }

            if (dummy.ParentBoneIndex != -1) continue;
            if (dummy.ParentBoneIndex < 0 || dummy.ParentBoneIndex >= flver.Nodes.Count)
            {
                hasChanged = true;
                Console.WriteLine($"Invalid parent node index {dummy.ParentBoneIndex} in dummy poly at index {i} with refId {dummy.ReferenceID}, setting to -1.");
                dummy.ParentBoneIndex = -1;
            }
            else
            {
                FLVER.Node node = flver.Nodes[dummy.ParentBoneIndex];
                hasChanged |= SetNodeFlag(node, FLVER.Node.NodeFlags.DummyOwner);
                callbacks.Add((dummy.ParentBoneIndex, x => dummy.ParentBoneIndex = x));
            }
        }

        for (short i = 0; i < flver.Nodes.Count; i++)
        {
            FLVER.Node node = flver.Nodes[i];

            callbacks.Add((node.ParentIndex, x => node.ParentIndex = x));
            callbacks.Add((node.FirstChildIndex, x => node.FirstChildIndex = x));
            callbacks.Add((node.PreviousSiblingIndex, x => node.PreviousSiblingIndex = x));
            callbacks.Add((node.NextSiblingIndex, x => node.NextSiblingIndex = x));
            
            if (node is { ParentIndex: -1, FirstChildIndex: -1, PreviousSiblingIndex: -1, NextSiblingIndex: -1, Flags: 0 })
            {
                hasChanged |= SetNodeFlag(node, FLVER.Node.NodeFlags.Disabled);
            }
        }
        
        callbacks.AddRange(flver.Skeletons.BaseSkeleton.Select(bone => ((int, Action<short>))(bone.NodeIndex, x => bone.NodeIndex = x)));
        callbacks.AddRange(flver.Skeletons.AllSkeletons.Select(bone => ((int, Action<short>))(bone.NodeIndex, x => bone.NodeIndex = x)));
        
        List<FLVER.Node> newNodeList = flver.Nodes.GroupBy(x => x.Flags.HasFlag(FLVER.Node.NodeFlags.Disabled))
            .OrderBy(x => x.Key).SelectMany(x => x).ToList();
        HashSet<int> remappedIndices = [];
        foreach ((int index, Action<short> setIndex) in callbacks)
        {
            hasChanged |= RemapNodeIndex(flver.Nodes, newNodeList, remappedIndices, index, setIndex);
        }
        flver.Nodes = newNodeList;
        
        short lastRootNodeIndex = (short)flver.Nodes.FindLastIndex(x =>
            x is { ParentIndex: -1, NextSiblingIndex: -1 } && x.PreviousSiblingIndex != -1);
        if (lastRootNodeIndex == -1)
        {
            lastRootNodeIndex = 0;
        }
        for (short i = 1; i < flver.Nodes.Count; i++)
        {
            FLVER.Node node = flver.Nodes[i];
            if (i == lastRootNodeIndex) continue;
            if (node is not { ParentIndex: -1, PreviousSiblingIndex: -1 } ||
                node.Flags.HasFlag(FLVER.Node.NodeFlags.Disabled)) continue;

            hasChanged = true;
            FLVER.Node lastRootNode = flver.Nodes[lastRootNodeIndex];
            Console.WriteLine($"Connecting node \"{node.Name}\" as next sibling of \"{lastRootNode.Name}\"");
            lastRootNode.NextSiblingIndex = i;
            node.PreviousSiblingIndex = lastRootNodeIndex;
            lastRootNodeIndex = i;
        }

        hasChanged |= AddNodesToSkeleton(flver, flver.Skeletons.BaseSkeleton);
        hasChanged |= AddNodesToSkeleton(flver, flver.Skeletons.AllSkeletons);
        
        return hasChanged;
    }

    private static bool AddNodesToSkeleton(FLVER2 flver, List<FLVER2.SkeletonSet.Bone> skeleton)
    {
        if (skeleton.Count == 0 || skeleton.Count == flver.Nodes.Count) return false;

        bool hasChanged = false;
        for (int i = 0; i < flver.Nodes.Count; i++)
        {
            if (skeleton.Any(bone => bone.NodeIndex == i)) continue;

            hasChanged = true;
            FLVER.Node node = flver.Nodes[i];
            Console.WriteLine($"Adding missing node {node.Name} to skeleton definition.");
            short parentIndex = (short)skeleton.FindIndex(x => x.NodeIndex == node.ParentIndex);
            short childIndex = (short)skeleton.FindIndex(x => x.NodeIndex == node.FirstChildIndex);
            short prevIndex = (short)skeleton.FindIndex(x => x.NodeIndex == node.PreviousSiblingIndex);
            short nextIndex = (short)skeleton.FindIndex(x => x.NodeIndex == node.NextSiblingIndex);

            if (prevIndex != -1)
            {
                skeleton[prevIndex].NextSiblingIndex = (short)skeleton.Count;
            }
                
            skeleton.Add(new FLVER2.SkeletonSet.Bone(i)
            {
                ParentIndex = parentIndex,
                FirstChildIndex = childIndex,
                PreviousSiblingIndex = prevIndex,
                NextSiblingIndex = nextIndex
            });
        }

        return hasChanged;
    }

    private record MeshInfo(FLVER2.Mesh Mesh, FLVER2.Material Material, FLVER2.GXList? GxList);
    
    private static bool RemoveEmptyMeshes(FLVER2 flver)
    {
        bool hasChanged = false;
        List<MeshInfo> meshes = [];
        for (int i = 0; i < flver.Meshes.Count; i++)
        {
            FLVER2.Mesh mesh = flver.Meshes[i];
            if (mesh.Vertices.Count == 0
                || mesh.FaceSets.Count == 0
                || mesh.FaceSets[0].Indices.Count == 0)
            {
                hasChanged = true;
                Console.WriteLine($"Removing empty mesh at index {i} with material {flver.Materials[mesh.MaterialIndex].Name}");
            }

            FLVER2.Material material = flver.Materials[mesh.MaterialIndex];
            FLVER2.GXList? gxList = material.GXIndex == -1 ? null : flver.GXLists[material.GXIndex];
            meshes.Add(new MeshInfo(mesh, material, gxList));
        }

        flver.Meshes.Clear();
        flver.Materials.Clear();
        flver.GXLists.Clear();
        
        foreach (MeshInfo meshInfo in meshes)
        {
            flver.Meshes.Add(meshInfo.Mesh);

            int materialIndex = flver.Materials.IndexOf(meshInfo.Material);
            if (materialIndex == -1)
            {
                materialIndex = flver.Materials.Count;
                flver.Materials.Add(meshInfo.Material);
            }

            meshInfo.Mesh.MaterialIndex = materialIndex;
            
            if (meshInfo.GxList is null) continue;

            int gxIndex = flver.GXLists.IndexOf(meshInfo.GxList);
            if (gxIndex == -1)
            {
                gxIndex = flver.GXLists.Count;
                flver.GXLists.Add(meshInfo.GxList);
            }

            meshInfo.Material.GXIndex = gxIndex;
        }

        return hasChanged;
    }
    
    private static bool FixFaceWinding(FLVER2 flver, List<int> fixFaceWinding)
    {
        if (fixFaceWinding[0] == -1) fixFaceWinding = Enumerable.Range(0, flver.Meshes.Count).ToList();
        
        bool hasChanged = false;
        foreach (int meshIndex in fixFaceWinding)
        {
            if (meshIndex < 0 || meshIndex > flver.Meshes.Count) Console.WriteLine($"Index {meshIndex} is out of range, cannot fix face winding.");
            hasChanged |= FixFaceWinding(flver, meshIndex);
        }

        return hasChanged;
    }

    private static bool FixFaceWinding(FLVER2 flver, int meshIndex)
    {
        FLVER2.Mesh mesh = flver.Meshes[meshIndex];
        
        //only flip per mesh since inconsistent winding is unlikely and backface detection doesn't cover all edge cases
        bool hasChanged = false;
        for (int i = 0; i < mesh.FaceSets.Count; i++)
        {
            FLVER2.FaceSet faceSet = mesh.FaceSets[i];
            if (faceSet.TriangleStrip) Console.WriteLine("Could not fix face winding for triangle strip.");
            
            double numNeedFlip = 0.0;
            for (int j = 0; j < faceSet.Indices.Count; j += 3)
            {
                //deleted faceset
                if (faceSet.Indices[j] == faceSet.Indices[j + 1]) break;

                FLVER.Vertex vert1 = mesh.Vertices[faceSet.Indices[j]];
                FLVER.Vertex vert2 = mesh.Vertices[faceSet.Indices[j + 1]];
                FLVER.Vertex vert3 = mesh.Vertices[faceSet.Indices[j + 2]];

                //CCW winding
                Vector3 faceNormal = Vector3.Cross(vert3.Position - vert1.Position, vert2.Position - vert1.Position);
                faceNormal = Vector3.Normalize(faceNormal);
                Vector3 vertexFaceNormal = (vert1.Normal + vert2.Normal + vert3.Normal) / 3;
                float dot = Vector3.Dot(vertexFaceNormal, faceNormal);

                numNeedFlip++;
            }

            if (numNeedFlip / faceSet.Indices.Count < 0.75) continue;
            
            hasChanged = true;
            Console.WriteLine($"Flipped faceset {i} in mesh at index {meshIndex} with material {flver.Materials[mesh.MaterialIndex]}");
            
            for (int j = 0; j < faceSet.Indices.Count; j += 3)
            {
                (faceSet.Indices[j + 1], faceSet.Indices[j + 2]) =
                    (faceSet.Indices[j + 2], faceSet.Indices[j + 1]);
            }
        }

        return hasChanged;
    }

    private static bool FixLods(FLVER2 flver, List<int> fixLods)
    {
        if (fixLods[0] == -1) fixLods = Enumerable.Range(0, flver.Meshes.Count).ToList();

        bool hasChanged = false;
        foreach (int meshIndex in fixLods)
        {
            if (meshIndex < 0 || meshIndex > flver.Meshes.Count) Console.WriteLine($"Index {meshIndex} is out of range, cannot fix LODs.");
            FixLods(flver, meshIndex);
        }

        return hasChanged;
    }

    public static void FixLods(FLVER2 flver, int meshIndex)
    {
        FLVER2.Mesh mesh = flver.Meshes[meshIndex];
        FLVER2.FaceSet.FSFlags[] faceSetFlags =
        {
            FLVER2.FaceSet.FSFlags.None,
            FLVER2.FaceSet.FSFlags.LodLevel1,
            FLVER2.FaceSet.FSFlags.LodLevel2,
            FLVER2.FaceSet.FSFlags.MotionBlur,
            FLVER2.FaceSet.FSFlags.MotionBlur | FLVER2.FaceSet.FSFlags.LodLevel1,
            FLVER2.FaceSet.FSFlags.MotionBlur | FLVER2.FaceSet.FSFlags.LodLevel2
        };
        
        if (mesh.FaceSets.Count == faceSetFlags.Length) return;
        Console.WriteLine($"Adding missing facesets to mesh at index {meshIndex} with material {flver.Materials[mesh.MaterialIndex]}");
        for (int i = mesh.FaceSets.Count; i < faceSetFlags.Length; i++)
        {
            mesh.FaceSets.Add(new FLVER2.FaceSet
            {
                Indices = mesh.FaceSets[0].Indices,
                CullBackfaces = mesh.FaceSets[0].CullBackfaces,
                Flags = faceSetFlags[i],
                TriangleStrip = mesh.FaceSets[0].TriangleStrip
            });
        }
    }
    
    private static bool FixDecals(FLVER2 flver, List<int> fixDecals)
    {
        if (fixDecals[0] == -1) fixDecals = Enumerable.Range(0, flver.Meshes.Count).ToList();

        bool hasChanged = false;
        foreach (int meshIndex in fixDecals)
        {
            if (meshIndex < 0 || meshIndex > flver.Meshes.Count) Console.WriteLine($"Index {meshIndex} is out of range, cannot fix decals.");
            FLVER2.Mesh mesh = flver.Meshes[meshIndex];
            //no decal uvs
            if (mesh.Vertices[0].UVs.Count < 2) continue;
            hasChanged = true;
            FixDecals(mesh);
        }

        return hasChanged;
    }

    private static void FixDecals(FLVER2.Mesh mesh)
    {
        //we have no good way to determine the uv index right now so we just assume index 1
        foreach (FLVER.Vertex vertex in mesh.Vertices)
        {
            vertex.UVs[1] = new Vector3();
        }
    }
}