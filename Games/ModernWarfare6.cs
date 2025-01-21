﻿using Cast.NET;
using Cast.NET.Nodes;
using DotnesktRemastered.Structures;
using Serilog;
using System.IO;
using System.Numerics;
using System.Xml.Linq;

namespace DotnesktRemastered.Games
{
    public class ModernWarfare6
    {
        public static CordycepProcess Cordycep = Program.Cordycep;

        private static uint GFXMAP_POOL_IDX = 50;

        public static void DumpMap(string name)
        {
            Log.Information("Finding map {baseName}...", name);
            Cordycep.EnumerableAssetPool(GFXMAP_POOL_IDX, (asset) =>
            {
                MW6GfxWorld gfxWorld = Cordycep.ReadMemory<MW6GfxWorld>(asset.Header);
                if (gfxWorld.baseName == 0) return;
                string baseName = Cordycep.ReadString(gfxWorld.baseName).Trim();
                if (baseName == name)
                {
                    DumpMap(gfxWorld, baseName);
                    Log.Information("Found map {baseName}.", baseName);
                }
            });
        }

        private static unsafe void DumpMap(MW6GfxWorld gfxWorld, string baseName)
        {

            MW6GfxWorldTransientZone[] transientZone = new MW6GfxWorldTransientZone[gfxWorld.transientZoneCount];
            for (int i = 0; i < gfxWorld.transientZoneCount; i++)
            {
                transientZone[i] = Cordycep.ReadMemory<MW6GfxWorldTransientZone>(gfxWorld.transientZones + i * sizeof(MW6GfxWorldTransientZone));
            }
            MW6GfxWorldSurfaces gfxWorldSurfaces = gfxWorld.surfaces;

            MW6GfxSurface[] surfaces = new MW6GfxSurface[gfxWorldSurfaces.count];
            for (int i = 0; i < gfxWorldSurfaces.count; i++)
            {
                surfaces[i] = Cordycep.ReadMemory<MW6GfxSurface>(gfxWorldSurfaces.surfaces + i * sizeof(MW6GfxSurface));
            }

            MeshNode[] meshes = new MeshNode[gfxWorldSurfaces.count];
            for (int i = 0; i < gfxWorldSurfaces.count; i++)
            {
                MW6GfxSurface gfxSurface = surfaces[i];

                MW6GfxUgbSurfData ugbSurfData = Cordycep.ReadMemory<MW6GfxUgbSurfData>(gfxWorldSurfaces.ugbSurfData + (nint)(gfxSurface.ugbSurfDataIndex * sizeof(MW6GfxUgbSurfData)));

                MW6GfxWorldDrawOffset worldDrawOffset = ugbSurfData.worldDrawOffset;

                MW6GfxWorldTransientZone zone = transientZone[ugbSurfData.transientZoneIndex];
                Log.Information("Processing mesh {i}, vertex count: {vertexCount}, tri count: {triCount}", i, gfxSurface.vertexCount, gfxSurface.triCount);

                MeshNode mesh = new MeshNode();
                mesh.AddValue("ul", ugbSurfData.layerCount);

                CastArrayProperty<Vector3> positions = mesh.AddArray<Vector3>("vp", new(gfxSurface.vertexCount));
                CastArrayProperty<Vector3> normals = mesh.AddArray<Vector3>("vn", new(gfxSurface.vertexCount));
                CastArrayProperty<ushort> faceIndices = mesh.AddArray<ushort>("f", new(gfxSurface.triCount * 3));

                for (int layerIdx = 0; layerIdx < ugbSurfData.layerCount; layerIdx++)
                {
                    mesh.AddArray<Vector2>($"u{layerIdx}", new(gfxSurface.vertexCount));
                }

                CastArrayProperty<Vector2> uvs = mesh.GetProperty<CastArrayProperty<Vector2>>("u0");

                nint xyzPtr = zone.drawVerts.posData + (nint)ugbSurfData.xyzOffset;
                nint tangentFramePtr = zone.drawVerts.posData + (nint)ugbSurfData.tangentFrameOffset;
                nint texCoordPtr = zone.drawVerts.posData + (nint)ugbSurfData.texCoordOffset;

                //test tangent frame
                for (int j = 0; j < gfxSurface.vertexCount; j++)
                {
                    uint packedPosition = Cordycep.ReadMemory<uint>(xyzPtr);
                    Vector3 position = new Vector3(
                        (float)((packedPosition >> 0) & 0x1FFFFF),
                        (float)((packedPosition >> 21) & 0x1FFFFF),
                        (float)((packedPosition >> 42) & 0x1FFFFF));

                    position *= worldDrawOffset.scale;
                    position += new Vector3(worldDrawOffset.x, worldDrawOffset.y, worldDrawOffset.z);

                    positions.Add(position);
                    xyzPtr += 4;

                    uint packedTangentFrame = Cordycep.ReadMemory<uint>(tangentFramePtr);
                    Vector3 normal = Utils.UnpackCoDQTangent(packedTangentFrame);

                    normals.Add(normal);
                    tangentFramePtr += 4;

                    Vector2 uv = Cordycep.ReadMemory<Vector2>(texCoordPtr);
                    uvs.Add(uv);
                    texCoordPtr += 8;

                    for (int layerIdx = 1; layerIdx < ugbSurfData.layerCount; layerIdx++)
                    {
                        Vector2 uvExtra = Cordycep.ReadMemory<Vector2>(texCoordPtr);
                        mesh.GetProperty<CastArrayProperty<Vector2>>($"u{layerIdx}").Add(uvExtra);
                        texCoordPtr += 8;
                    }
                }

                nint indiciesPtr = (nint)(zone.drawVerts.indices + gfxSurface.baseIndex * 2);

                for (int j = 0; j < gfxSurface.triCount; j++)
                {
                    ushort index1 = Cordycep.ReadMemory<ushort>(indiciesPtr);
                    faceIndices.Add(index1);
                    indiciesPtr += 2;
                    ushort index2 = Cordycep.ReadMemory<ushort>(indiciesPtr);
                    faceIndices.Add(index2);
                    indiciesPtr += 2;
                    ushort index3 = Cordycep.ReadMemory<ushort>(indiciesPtr);
                    faceIndices.Add(index3);
                    indiciesPtr += 2;
                }

                meshes[i] = mesh;
            }

            //Write to file

            ModelNode model = new ModelNode();
            SkeletonNode skeleton = new SkeletonNode();
            model.AddString("n", $"{baseName}_base_mesh");
            model.AddNode(skeleton);
            foreach(MeshNode mesh in meshes)
            {
                model.AddNode(mesh);
            }
            CastNode root = new CastNode(CastNodeIdentifier.Root);
            root.AddNode(model);
            CastWriter.Save(@"D:/" + baseName + ".cast", root);
        }
    }
}
