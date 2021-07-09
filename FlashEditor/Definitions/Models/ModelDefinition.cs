using System;
using OpenTK;

namespace FlashEditor.Definitions.Model {

    public class Model {
        public int[] anIntArray1231;
        public int anInt1234;
        public byte[] aByteArray1266;
        public byte[] aByteArray1241;
        public byte[] aByteArray1243;
        public float[] texCoordU;
        public float[] texCoordV;
        public bool newFormat;
        public int version = 12;
        public short[] triangleViewspaceX;
        public short[] triangleViewspaceY;
        public short[] triangleViewspaceZ;
        public ushort[] textureTrianglePIndex;
        public ushort[] textureTriangleMIndex;
        public ushort[] textureTriangleNIndex;
        public int[] verticesX;
        public int[] verticesY;
        public int[] verticesZ;
        public short[] colorValues;
        public short[] faceTexture;
        public int[] vertexSkins;
        public short[] textureCoords;
        public byte[] faceRenderType;
        public sbyte[] textureRenderTypes;
        public sbyte[] faceAlpha;
        public sbyte[] trianglePriorities;
        public int[] triangleSkinValues;
        public int numVertices;
        public int numTriangles;
        public int numTextureTriangles;
        public int maxDepth;
        public sbyte priority;
        public int[] particleDirectionZ;
        public sbyte[] particleLifespanY;
        public int[] particleDirectionX;
        public sbyte[] particleLifespanX;
        public int[] particleLifespanZ;
        public int[] particleDirectionY;
        public int[] texturePrimaryColor;
        public int[] textureSecondaryColor;
        public Surface[] surfaces;
        public SurfaceSkin[] surfaceSkins;
        public VertexNormal[] isolatedVertexNormals;
        private bool useEffects = true;
        public static int[] hsl2rgb;

        static Model() {
            int[] out1 = hsl2rgb = new int[65536];
            double d = 0.7D;
            int i = 0;

            for(int i1 = 0; i1 != 512; ++i1) {
                float f = ((float)(i1 >> 3) / 64.0F + 0.0078125F) * 360.0F;
                float f1 = 0.0625F + (float)(7 & i1) / 8.0F;

                for(int i2 = 0; i2 != 128; ++i2) {
                    float f2 = (float)i2 / 128.0F;
                    float f3, f4, f5;
                    float f6 = f / 60.0F;
                    int i3 = (int)f6;
                    int i4 = i3 % 6;
                    float f7 = f6 - (float)i3;
                    float f8 = f2 * (-f1 + 1.0F);
                    float f9 = f2 * (-(f7 * f1) + 1.0F);
                    float f10 = (1.0F - f1 * (-f7 + 1.0F)) * f2;
                    if(i4 == 0) {
                        f3 = f2;
                        f5 = f8;
                        f4 = f10;
                    } else if(i4 == 1) {
                        f5 = f8;
                        f3 = f9;
                        f4 = f2;
                    } else if(i4 == 2) {
                        f3 = f8;
                        f4 = f2;
                        f5 = f10;
                    } else if(i4 == 3) {
                        f4 = f9;
                        f3 = f8;
                        f5 = f2;
                    } else if(i4 == 4) {
                        f5 = f2;
                        f3 = f10;
                        f4 = f8;
                    } else {
                        f4 = f8;
                        f5 = f9;
                        f3 = f2;
                    }

                    out1[i++] = (int) ((float) Math.Pow((double) f3, d) * 256.0F) << 16
                            | (int) ((float) Math.Pow((double) f4, d) * 256.0F) << 8
                            | (int) ((float) Math.Pow((double) f5, d) * 256.0F);
                }
            }

        }

        public Model() {
        }

        public Model(sbyte[] data) {
            if(UsesNewerHeader(data) && !UsesNewHeader(data)) {
                newFormat = true;
                if(data[0] == 1)
                    Read800Model(data);
                else if(data[0] == 0)
                    DecodeNewOldModel(data);
            } else {
                newFormat = false;
                if(this.UsesNewHeader(data)) {
                    this.DecodeNew(data);
                    if(this.version < 14)
                        this.Upscale();
                } else {
                    this.DecodeOld(data);
                    this.Upscale();
                }
            }
        }

        private bool UsesNewerHeader(sbyte[] data) {
            return data[0] == 1 || data[0] == 0;
        }

        private bool UsesNewHeader(sbyte[] data) {
            return data[data.Length - 2] == -1 && data[data.Length - 1] == -1;
        }

        private void DecodeNew(sbyte[] data) {
            JagStream buffer = new JagStream((byte[]) (Array) data);
            JagStream buffer_25_ = new JagStream((byte[]) (Array) data);
            JagStream buffer_26_ = new JagStream((byte[]) (Array) data);
            JagStream buffer_27_ = new JagStream((byte[]) (Array) data);
            JagStream buffer_28_ = new JagStream((byte[]) (Array) data);
            JagStream buffer_29_ = new JagStream((byte[]) (Array) data);
            JagStream buffer_30_ = new JagStream((byte[]) (Array) data);

            buffer.Position = data.Length - 23;

            numVertices = buffer.ReadUnsignedShort();
            numTriangles = buffer.ReadUnsignedShort();
            numTextureTriangles = buffer.ReadUnsignedByte();
            int flag = buffer.ReadUnsignedByte();
            bool has_fill_attr = (flag & 1) != 0;
            bool has_surface_fx = (flag & 2) != 0;
            bool has_vertex_normals = (flag & 4) != 0;
            bool has_large_size = (flag & 8) != 0;
            if(has_large_size) {
                buffer.Position -= 7;
                this.version = buffer.ReadUnsignedByte();
                buffer.Position += 6;
            }

            int model_priority = buffer.ReadUnsignedByte();
            if(model_priority == 255)
                model_priority = -1;

            int has_alpha = buffer.ReadUnsignedByte();
            int has_triangle_skin_types = buffer.ReadUnsignedByte();
            int has_textures = buffer.ReadUnsignedByte();
            int has_vertex_skin_types = buffer.ReadUnsignedByte();
            int i_40_ = buffer.ReadUnsignedShort();
            int i_41_ = buffer.ReadUnsignedShort();
            int i_42_ = buffer.ReadUnsignedShort();
            int i_43_ = buffer.ReadUnsignedShort();
            int i_44_ = buffer.ReadUnsignedShort();
            int i_45_ = 0;
            int particle_count = 0;
            int particle_color = 0;
            int tex_tri_count;
            if(this.numTextureTriangles != 0) {
                this.textureRenderTypes = new sbyte[this.numTextureTriangles];
                buffer.Position = 0;

                for(tex_tri_count = 0; tex_tri_count != this.numTextureTriangles; ++tex_tri_count) {
                    sbyte type = this.textureRenderTypes[tex_tri_count] = (sbyte)buffer.ReadByte();

                    if(type == 0)
                        i_45_++;
                    if(type >= 1 && type <= 3)
                        particle_count++;
                    if(type == 2)
                        particle_color++;

                }
            }

            tex_tri_count = numTextureTriangles;
            int var63 = tex_tri_count;
            tex_tri_count += numVertices;
            int i_51_ = tex_tri_count;
            if(has_fill_attr)
                tex_tri_count += numTriangles;

            int i_52_ = tex_tri_count;
            tex_tri_count += numTriangles;
            int i_53_ = tex_tri_count;
            if(model_priority == -1)
                tex_tri_count += numTriangles;

            int i_54_ = tex_tri_count;
            if(has_triangle_skin_types == 1)
                tex_tri_count += numTriangles;

            int i_55_ = tex_tri_count;
            if(has_vertex_skin_types == 1)
                tex_tri_count += numVertices;

            int i_56_ = tex_tri_count;
            if(has_alpha == 1)
                tex_tri_count += numTriangles;

            int i_57_ = tex_tri_count;
            tex_tri_count += i_43_;
            int i_58_ = tex_tri_count;
            if(has_textures == 1)
                tex_tri_count += numTriangles * 2;

            int i_59_ = tex_tri_count;
            tex_tri_count += i_44_;
            int i_60_ = tex_tri_count;
            tex_tri_count += 2 * numTriangles;
            int i_61_ = tex_tri_count;
            tex_tri_count += i_40_;
            int i_62_ = tex_tri_count;
            tex_tri_count += i_41_;
            int i_63_ = tex_tri_count;
            tex_tri_count += i_42_;
            int i_64_ = tex_tri_count;
            tex_tri_count += 6 * i_45_;
            int i_65_ = tex_tri_count;
            tex_tri_count += particle_count * 6;
            sbyte i_66_ = 6;
            if(this.version == 14)
                i_66_ = 7;
            else if(this.version >= 15)
                i_66_ = 9;

            int i_67_ = tex_tri_count;
            tex_tri_count += particle_count * i_66_;
            int i_68_ = tex_tri_count;
            tex_tri_count += particle_count;
            int i_69_ = tex_tri_count;
            tex_tri_count += particle_count;
            int i_70_ = tex_tri_count;
            tex_tri_count += particle_count + particle_color * 2;
            this.colorValues = new short[this.numTriangles];
            buffer.Position = var63;
            if(has_fill_attr)
                this.faceRenderType = new byte[numTriangles];

            if(this.numTextureTriangles != 0) {
                textureTrianglePIndex = new ushort[numTextureTriangles];
                textureTriangleMIndex = new ushort[numTextureTriangles];
                textureTriangleNIndex = new ushort[numTextureTriangles];
                if(particle_count != 0) {
                    particleDirectionZ = new int[particle_count];
                    particleLifespanY = new sbyte[particle_count];
                    particleDirectionX = new int[particle_count];
                    particleLifespanX = new sbyte[particle_count];
                    particleLifespanZ = new int[particle_count];
                    particleDirectionY = new int[particle_count];
                }

                if(particle_color != 0) {
                    texturePrimaryColor = new int[particle_color];
                    textureSecondaryColor = new int[particle_color];
                }
            }

            verticesX = new int[numVertices];
            verticesY = new int[numVertices];
            verticesZ = new int[numVertices];
            if(has_vertex_skin_types == 1)
                vertexSkins = new int[numVertices];

            triangleViewspaceX = new short[numTriangles];
            triangleViewspaceY = new short[numTriangles];
            triangleViewspaceZ = new short[numTriangles];
            if(has_textures == 1)
                this.faceTexture = new short[this.numTriangles];

            this.priority = (sbyte) model_priority;
            if(model_priority == -1)
                this.trianglePriorities = new sbyte[this.numTriangles];

            if(has_alpha == 1)
                this.faceAlpha = new sbyte[this.numTriangles];

            if(has_textures == 1 && this.numTextureTriangles != 0)
                this.textureCoords = new short[this.numTriangles];

            if(has_triangle_skin_types == 1)
                this.triangleSkinValues = new int[this.numTriangles];

            buffer_25_.Position = i_61_;
            buffer_26_.Position = i_62_;
            buffer_27_.Position = i_63_;
            buffer_28_.Position = i_55_;
            int vertex_x = 0;
            int vertex_y = 0;
            int vertex_z = 0;

            int tri_x;
            int isolated_normal_count;
            for(tri_x = 0; tri_x != this.numVertices; ++tri_x) {
                int tri_y = buffer.ReadUnsignedByte();
                int tri_z = (tri_y & 1) != 0 ? buffer_25_.ReadUnsignedSmart() : 0;
                int prev_z_view = (tri_y & 2) != 0 ? buffer_26_.ReadUnsignedSmart()
                        : 0;
                isolated_normal_count = (tri_y & 4) != 0 ? buffer_27_
                        .ReadUnsignedSmart() : 0;
                vertex_x += tri_z;
                vertex_y += prev_z_view;
                vertex_z += isolated_normal_count;
                this.verticesX[tri_x] = vertex_x;
                this.verticesY[tri_x] = vertex_y;
                this.verticesZ[tri_x] = vertex_z;
                if(has_vertex_skin_types == 1)
                    this.vertexSkins[tri_x] = buffer_28_.ReadByte();
            }

            buffer.Position = i_60_;
            buffer_25_.Position = i_51_;
            buffer_26_.Position = i_53_;
            buffer_27_.Position = i_56_;
            buffer_28_.Position = i_54_;
            buffer_29_.Position = i_58_;
            buffer_30_.Position = i_59_;

            for(tri_x = 0; tri_x != this.numTriangles; ++tri_x) {
                this.colorValues[tri_x] = (short) buffer.ReadUnsignedShort();
                if(has_fill_attr)
                    this.faceRenderType[tri_x] = (byte) buffer_25_.ReadByte();

                if(model_priority == -1)
                    this.trianglePriorities[tri_x] = (sbyte) buffer_26_.ReadByte();

                if(has_alpha == 1) {
                    this.faceAlpha[tri_x] = (sbyte) buffer_27_.ReadByte();
                }

                if(has_triangle_skin_types == 1) {
                    this.triangleSkinValues[tri_x] = buffer_28_.ReadByte();
                }

                if(has_textures == 1) {
                    this.faceTexture[tri_x] = (short) (buffer_29_
                            .ReadUnsignedShort() - 1);
                }

                if(this.textureCoords != null) {
                    if(this.faceTexture[tri_x] == -1) {
                        this.textureCoords[tri_x] = -1;
                    } else {
                        this.textureCoords[tri_x] = (sbyte) (buffer_30_
                                .ReadUnsignedByte() - 1);
                    }
                }
            }

            this.maxDepth = -1;
            buffer.Position = i_57_;
            buffer_25_.Position = i_52_;
            short var67 = 0;
            short var66 = 0;
            short var65 = 0;
            short var64 = 0;

            int isolated_normal;
            for(isolated_normal_count = 0; isolated_normal_count != this.numTriangles; ++isolated_normal_count) {
                isolated_normal = buffer_25_.ReadUnsignedByte();

                if(isolated_normal == 1) {
                    var67 = (short) (var64 + buffer.ReadUnsignedSmart());
                    var66 = (short) (var67 + buffer.ReadUnsignedSmart());
                    var65 = (short) (var66 + buffer.ReadUnsignedSmart());
                    var64 = var65;
                    this.triangleViewspaceX[isolated_normal_count] = var67;
                    this.triangleViewspaceY[isolated_normal_count] = var66;
                    this.triangleViewspaceZ[isolated_normal_count] = var65;
                    if(this.maxDepth < var67) {
                        this.maxDepth = var67;
                    }

                    if(this.maxDepth < var66) {
                        this.maxDepth = var66;
                    }

                    if(this.maxDepth < var65) {
                        this.maxDepth = var65;
                    }
                }
                if(isolated_normal == 2) {
                    var66 = var65;
                    var65 = (short) (buffer.ReadUnsignedSmart() + var64);
                    var64 = var65;
                    this.triangleViewspaceX[isolated_normal_count] = var67;
                    this.triangleViewspaceY[isolated_normal_count] = var66;
                    this.triangleViewspaceZ[isolated_normal_count] = var65;
                    if(this.maxDepth < var65) {
                        this.maxDepth = var65;
                    }
                }
                if(isolated_normal == 3) {
                    var67 = var65;
                    var65 = (short) (buffer.ReadUnsignedSmart() + var64);
                    var64 = var65;
                    this.triangleViewspaceX[isolated_normal_count] = var67;
                    this.triangleViewspaceY[isolated_normal_count] = var66;
                    this.triangleViewspaceZ[isolated_normal_count] = var65;
                    if(this.maxDepth < var65) {
                        this.maxDepth = var65;
                    }
                }
                if(isolated_normal == 4) {
                    short x = var67;
                    var67 = var66;
                    var65 = (short) (buffer.ReadUnsignedSmart() + var64);
                    var66 = x;
                    var64 = var65;
                    this.triangleViewspaceX[isolated_normal_count] = var67;
                    this.triangleViewspaceY[isolated_normal_count] = x;
                    this.triangleViewspaceZ[isolated_normal_count] = var65;
                    if(this.maxDepth < var65) {
                        this.maxDepth = var65;
                    }
                }
            }

            buffer.Position = i_64_;
            ++this.maxDepth;
            buffer_25_.Position = i_65_;
            buffer_26_.Position = i_67_;
            buffer_27_.Position = i_68_;
            buffer_28_.Position = i_69_;
            buffer_29_.Position = i_70_;

            for(isolated_normal_count = 0; isolated_normal_count != this.numTextureTriangles; ++isolated_normal_count) {
                sbyte var70 = this.textureRenderTypes[isolated_normal_count];

                if(var70 == 0) {
                    this.textureTrianglePIndex[isolated_normal_count] = (ushort) buffer
                            .ReadUnsignedShort();
                    this.textureTriangleMIndex[isolated_normal_count] = (ushort) buffer
                            .ReadUnsignedShort();
                    this.textureTriangleNIndex[isolated_normal_count] = (ushort) buffer
                            .ReadUnsignedShort();
                }
                if(var70 == 1) {
                    this.textureTrianglePIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    this.textureTriangleMIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    this.textureTriangleNIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    if(this.version < 15) {
                        this.particleDirectionX[isolated_normal_count] = buffer_26_
                                .ReadUnsignedShort();
                        this.particleDirectionY[isolated_normal_count] = this.version < 14 ? buffer_26_
                                .ReadUnsignedShort() : buffer_26_.ReadMedium();
                        this.particleDirectionZ[isolated_normal_count] = buffer_26_
                                .ReadUnsignedShort();
                    } else {
                        this.particleDirectionX[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                        this.particleDirectionY[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                        this.particleDirectionZ[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                    }

                    this.particleLifespanX[isolated_normal_count] = (sbyte) buffer_27_
                            .ReadByte();
                    this.particleLifespanY[isolated_normal_count] = (sbyte) buffer_28_
                            .ReadByte();
                    this.particleLifespanZ[isolated_normal_count] = (sbyte) buffer_29_
                            .ReadUnsignedByte();
                }
                if(var70 == 2) {
                    this.textureTrianglePIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    this.textureTriangleMIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    this.textureTriangleNIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    if(this.version < 15) {
                        this.particleDirectionX[isolated_normal_count] = buffer_26_
                                .ReadUnsignedShort();
                        this.particleDirectionY[isolated_normal_count] = this.version < 14 ? buffer_26_
                                .ReadUnsignedShort() : buffer_26_.ReadMedium();
                        this.particleDirectionZ[isolated_normal_count] = buffer_26_
                                .ReadUnsignedShort();
                    } else {
                        this.particleDirectionX[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                        this.particleDirectionY[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                        this.particleDirectionZ[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                    }

                    this.particleLifespanX[isolated_normal_count] = (sbyte) buffer_27_
                            .ReadByte();
                    this.particleLifespanY[isolated_normal_count] = (sbyte) buffer_28_
                            .ReadByte();
                    this.particleLifespanZ[isolated_normal_count] = buffer_29_
                            .ReadByte();
                    this.texturePrimaryColor[isolated_normal_count] = buffer_29_
                            .ReadByte();
                    this.textureSecondaryColor[isolated_normal_count] = buffer_29_
                            .ReadByte();
                }
                if(var70 == 3) {
                    this.textureTrianglePIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    this.textureTriangleMIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    this.textureTriangleNIndex[isolated_normal_count] = (ushort) buffer_25_
                            .ReadUnsignedShort();
                    if(this.version < 15) {
                        this.particleDirectionX[isolated_normal_count] = buffer_26_
                                .ReadUnsignedShort();
                        if(this.version < 14) {
                            this.particleDirectionY[isolated_normal_count] = buffer_26_
                                    .ReadUnsignedShort();
                        } else {
                            this.particleDirectionY[isolated_normal_count] = buffer_26_
                                    .ReadMedium();
                        }

                        this.particleDirectionZ[isolated_normal_count] = buffer_26_
                                .ReadUnsignedShort();
                    } else {
                        this.particleDirectionX[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                        this.particleDirectionY[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                        this.particleDirectionZ[isolated_normal_count] = buffer_26_
                                .ReadMedium();
                    }

                    this.particleLifespanX[isolated_normal_count] = (sbyte) buffer_27_
                            .ReadByte();
                    this.particleLifespanY[isolated_normal_count] = (sbyte) buffer_28_
                            .ReadByte();
                    this.particleLifespanZ[isolated_normal_count] = buffer_29_
                            .ReadByte();
                }
            }

            buffer.Position = tex_tri_count;
            int y;
            int var68;
            int var69;
            if(has_surface_fx) {
                isolated_normal_count = buffer.ReadUnsignedByte();
                if(isolated_normal_count != 0) {
                    this.surfaces = new Surface[isolated_normal_count];

                    for(isolated_normal = 0; isolated_normal != isolated_normal_count; ++isolated_normal) {
                        var69 = buffer.ReadUnsignedShort();
                        y = buffer.ReadUnsignedShort();
                        sbyte z = model_priority == -1 ? this.trianglePriorities[y]
                                : (sbyte)model_priority;
                        this.surfaces[isolated_normal] = new Surface(var69, y, (byte) z);
                    }
                }

                isolated_normal = buffer.ReadUnsignedByte();
                if(isolated_normal != 0) {
                    this.surfaceSkins = new SurfaceSkin[isolated_normal];

                    for(var69 = 0; var69 != isolated_normal; ++var69) {
                        y = buffer.ReadUnsignedShort();
                        var68 = buffer.ReadUnsignedShort();
                        this.surfaceSkins[var69] = new SurfaceSkin(y, var68);
                    }
                }
            }

            if(has_vertex_normals) {
                isolated_normal_count = buffer.ReadUnsignedByte();
                if(isolated_normal_count != 0) {
                    this.isolatedVertexNormals = new VertexNormal[isolated_normal_count];

                    for(isolated_normal = 0; isolated_normal != isolated_normal_count; ++isolated_normal) {
                        var69 = buffer.ReadUnsignedShort();
                        y = buffer.ReadUnsignedShort();
                        var68 = buffer.ReadUnsignedByte();
                        sbyte divisor = (sbyte)buffer.ReadByte();
                        this.isolatedVertexNormals[isolated_normal] = new VertexNormal(
                                var69, y, var68, (byte) divisor);
                    }
                }
            }

        }

        private void DecodeOld(sbyte[] data) {
            bool has_fill_attr = false;
            bool textured = false;
            JagStream buffer = new JagStream((byte[]) (Array) data);
            JagStream buffer_144_ = new JagStream((byte[]) (Array) data);
            JagStream buffer_145_ = new JagStream((byte[]) (Array) data);
            JagStream buffer_146_ = new JagStream((byte[]) (Array) data);
            JagStream buffer_147_ = new JagStream((byte[]) (Array) data);
            buffer.Position = data.Length - 18;
            this.numVertices = buffer.ReadUnsignedShort();
            this.numTriangles = buffer.ReadUnsignedShort();
            this.numTextureTriangles = buffer.ReadUnsignedByte();
            int has_textures = buffer.ReadUnsignedByte();
            int model_priority = buffer.ReadUnsignedByte();
            if(model_priority == 255) {
                model_priority = -1;
            }

            int has_alpha = buffer.ReadUnsignedByte();
            int has_triangle_skins = buffer.ReadUnsignedByte();
            int has_vertex_skin_types = buffer.ReadUnsignedByte();
            int i_153_ = buffer.ReadUnsignedShort();
            int i_154_ = buffer.ReadUnsignedShort();
            int i_155_ = buffer.ReadUnsignedShort();
            int i_156_ = buffer.ReadUnsignedShort();
            sbyte i_157_ = 0;
            int var42 = i_157_ + this.numVertices;
            int i_159_ = var42;
            var42 += this.numTriangles;
            int priority_buffer_pos = var42;
            if(model_priority == -1) {
                var42 += this.numTriangles;
            }

            int tskin_buffer_pos = var42;
            if(has_triangle_skins == 1) {
                var42 += this.numTriangles;
            }

            int draw_type_buffer_pos = var42;
            if(has_textures == 1) {
                var42 += this.numTriangles;
            }

            int i_163_ = var42;
            if(has_vertex_skin_types == 1) {
                var42 += this.numVertices;
            }

            int alpha_buffer_pos = var42;
            if(has_alpha == 1) {
                var42 += this.numTriangles;
            }

            int i_165_ = var42;
            var42 += i_156_;
            int triangle_color_buffer_pos = var42;
            var42 += this.numTriangles * 2;
            int texture_map_buffer_pos = var42;
            var42 += this.numTextureTriangles * 6;
            int i_168_ = var42;
            var42 += i_153_;
            int i_169_ = var42;
            var42 += i_154_;
            int var10000 = var42 + i_155_;
            this.priority = (sbyte) model_priority;
            if(model_priority == -1) {
                this.trianglePriorities = new sbyte[this.numTriangles];
            }

            if(has_triangle_skins == 1) {
                this.triangleSkinValues = new int[this.numTriangles];
            }

            if(has_vertex_skin_types == 1) {
                this.vertexSkins = new int[this.numVertices];
            }

            this.verticesX = new int[this.numVertices];
            this.verticesY = new int[this.numVertices];
            this.verticesZ = new int[this.numVertices];
            this.triangleViewspaceX = new short[this.numTriangles];
            this.triangleViewspaceY = new short[this.numTriangles];
            this.triangleViewspaceZ = new short[this.numTriangles];
            this.colorValues = new short[this.numTriangles];
            if(this.numTextureTriangles > 0) {
                this.textureTriangleMIndex = new ushort[this.numTextureTriangles];
                this.textureTriangleNIndex = new ushort[this.numTextureTriangles];
                this.textureTrianglePIndex = new ushort[this.numTextureTriangles];
            }

            if(has_textures == 1) {
                this.faceRenderType = new byte[this.numTriangles];
                this.faceTexture = new short[this.numTriangles];
                this.textureCoords = new short[this.numTriangles];
            }

            if(has_alpha == 1) {
                this.faceAlpha = new sbyte[this.numTriangles];
            }

            buffer.Position = i_157_;
            buffer_144_.Position = i_168_;
            buffer_145_.Position = i_169_;
            buffer_146_.Position = var42;
            buffer_147_.Position = i_163_;
            int vertex_x = 0;
            int vertex_y = 0;
            int vertex_z = 0;

            int tri_x;
            int bool_188_;
            int tri_y;
            for(tri_x = 0; tri_x != this.numVertices; ++tri_x) {
                tri_y = buffer.ReadUnsignedByte();
                int tri_z = (tri_y & 1) != 0 ? buffer_144_.ReadUnsignedSmart() : 0;
                int prev_z_view = (tri_y & 2) != 0 ? buffer_145_.ReadUnsignedSmart()
                        : 0;
                bool_188_ = (tri_y & 4) != 0 ? buffer_146_.ReadUnsignedSmart() : 0;
                vertex_x += tri_z;
                vertex_y += prev_z_view;
                vertex_z += bool_188_;
                this.verticesX[tri_x] = vertex_x;
                this.verticesY[tri_x] = vertex_y;
                this.verticesZ[tri_x] = vertex_z;
                if(has_vertex_skin_types == 1) {
                    this.vertexSkins[tri_x] = buffer_147_.ReadByte();
                }
            }

            buffer.Position = triangle_color_buffer_pos;
            buffer_144_.Position = draw_type_buffer_pos;
            buffer_145_.Position = priority_buffer_pos;
            buffer_146_.Position = alpha_buffer_pos;
            buffer_147_.Position = tskin_buffer_pos;

            for(tri_x = 0; tri_x != this.numTriangles; ++tri_x) {
                this.colorValues[tri_x] = (short) buffer.ReadUnsignedShort();
                if(has_textures == 1) {
                    tri_y = buffer_144_.ReadUnsignedByte();
                    if((tri_y & 1) == 0) {
                        this.faceRenderType[tri_x] = 0;
                    } else {
                        this.faceRenderType[tri_x] = 1;
                        has_fill_attr = true;
                    }

                    if((tri_y & 2) != 0) {
                        this.textureCoords[tri_x] = (sbyte) (tri_y >> 2);
                        this.faceTexture[tri_x] = this.colorValues[tri_x];
                        this.colorValues[tri_x] = 127;
                        if(this.faceTexture[tri_x] != -1) {
                            textured = true;
                        }
                    } else {
                        this.textureCoords[tri_x] = -1;
                        this.faceTexture[tri_x] = -1;
                    }
                }

                if(model_priority == -1) {
                    this.trianglePriorities[tri_x] = (sbyte) buffer_145_.ReadByte();
                }

                if(has_alpha == 1) {
                    this.faceAlpha[tri_x] = (sbyte) buffer_146_.ReadByte();
                }

                if(has_triangle_skins == 1) {
                    this.triangleSkinValues[tri_x] = buffer_147_.ReadByte();
                }
            }

            this.maxDepth = -1;
            buffer.Position = i_165_;
            buffer_144_.Position = i_159_;
            short var43 = 0;
            short var44 = 0;
            short var46 = 0;
            short var45 = 0;

            int tri;
            for(bool_188_ = 0; bool_188_ != this.numTriangles; ++bool_188_) {
                tri = buffer_144_.ReadUnsignedByte();
                if(tri == 1) {
                    var43 = (short) (buffer.ReadUnsignedSmart() + var45);
                    var44 = (short) (buffer.ReadUnsignedSmart() + var43);
                    var46 = (short) (buffer.ReadUnsignedSmart() + var44);
                    var45 = var46;
                    this.triangleViewspaceX[bool_188_] = var43;
                    this.triangleViewspaceY[bool_188_] = var44;
                    this.triangleViewspaceZ[bool_188_] = var46;
                    if(this.maxDepth < var43) {
                        this.maxDepth = var43;
                    }

                    if(this.maxDepth < var44) {
                        this.maxDepth = var44;
                    }

                    if(this.maxDepth < var46) {
                        this.maxDepth = var46;
                    }
                }
                if(tri == 2) {
                    var44 = var46;
                    var46 = (short) (buffer.ReadUnsignedSmart() + var45);
                    var45 = var46;
                    this.triangleViewspaceX[bool_188_] = var43;
                    this.triangleViewspaceY[bool_188_] = var44;
                    this.triangleViewspaceZ[bool_188_] = var46;
                    if(this.maxDepth < var46) {
                        this.maxDepth = var46;
                    }
                }
                if(tri == 3) {
                    var43 = var46;
                    var46 = (short) (buffer.ReadUnsignedSmart() + var45);
                    var45 = var46;
                    this.triangleViewspaceX[bool_188_] = var43;
                    this.triangleViewspaceY[bool_188_] = var44;
                    this.triangleViewspaceZ[bool_188_] = var46;
                    if(this.maxDepth < var46) {
                        this.maxDepth = var46;
                    }
                }
                if(tri == 4) {
                    short ptr_mask = var43;
                    var43 = var44;
                    var46 = (short) (buffer.ReadUnsignedSmart() + var45);
                    var44 = ptr_mask;
                    var45 = var46;
                    this.triangleViewspaceX[bool_188_] = var43;
                    this.triangleViewspaceY[bool_188_] = ptr_mask;
                    this.triangleViewspaceZ[bool_188_] = var46;
                    if(this.maxDepth < var46) {
                        this.maxDepth = var46;
                    }
                }
            }

            buffer.Position = texture_map_buffer_pos;
            ++this.maxDepth;

            for(bool_188_ = 0; bool_188_ != this.numTextureTriangles; ++bool_188_) {
                this.textureTrianglePIndex[bool_188_] = (ushort) buffer
                        .ReadUnsignedShort();
                this.textureTriangleMIndex[bool_188_] = (ushort) buffer
                        .ReadUnsignedShort();
                this.textureTriangleNIndex[bool_188_] = (ushort) buffer
                        .ReadUnsignedShort();
            }

            if(this.textureCoords != null) {
                bool var48 = false;

                for(tri = 0; tri != this.numTriangles; ++tri) {
                    int var47 = this.textureCoords[tri] & 255;
                    if(var47 != 255) {
                        if(this.textureTrianglePIndex[var47] == this.triangleViewspaceX[tri]
                                && this.triangleViewspaceY[tri] == this.textureTriangleMIndex[var47]
                                && this.triangleViewspaceZ[tri] == this.textureTriangleNIndex[var47]) {
                            this.textureCoords[tri] = -1;
                        } else {
                            var48 = true;
                        }
                    }
                }

                if(!var48) {
                    this.textureCoords = null;
                }
            }

            if(!has_fill_attr) {
                this.faceRenderType = null;
            }

            if(!textured) {
                this.faceTexture = null;
            }

        }

        private void Upscale() {
            for(int i = 0; i != this.numVertices; ++i) {
                this.verticesX[i] <<= 2;
                this.verticesY[i] <<= 2;
                this.verticesZ[i] <<= 2;
            }

        }

        void DecodeNewNewModel(sbyte[] modelData) {
            JagStream footer = new JagStream((byte[])(Array)modelData);
            JagStream drawTypes = new JagStream((byte[])(Array)modelData);
            JagStream priorities = new JagStream((byte[])(Array)modelData);
            JagStream alphas = new JagStream((byte[])(Array)modelData);
            JagStream trianglesAndVertices = new JagStream((byte[])(Array)modelData);
            JagStream textures = new JagStream((byte[])(Array)modelData);
            JagStream textureCoordinates = new JagStream((byte[])(Array)modelData);

            int i = footer.ReadUnsignedByte();
            if(i != 1)
                Console.WriteLine(i);
            else {
                footer.ReadUnsignedByte();// dummy read in
                version = footer.ReadUnsignedByte();// version now reads here instead

                footer.Position = (modelData.Length - 26);
                numVertices = footer.ReadUnsignedShort();
                numTriangles = footer.ReadUnsignedShort();
                numTextureTriangles = footer.ReadUnsignedShort();
                int footerFlags = footer.ReadUnsignedByte();
                bool hasFillAttributes = (footerFlags & 0x1) == 1;
                bool hasSurfaceEffects = (footerFlags & 0x2) == 2;
                bool hasVertexNormals = (footerFlags & 0x4) == 4;
                bool hasManyVertices = (footerFlags & 0x10) == 16;
                bool hasManyTriangles = (footerFlags & 0x20) == 32;
                bool hasManyVertexNormals = (footerFlags & 0x40) == 64;
                int modelPriority = footer.ReadUnsignedByte();
                int modelAlpha = footer.ReadUnsignedByte();
                int modelSkinValue = footer.ReadUnsignedByte();
                int modelTexture = footer.ReadUnsignedByte();
                int modelSkins = footer.ReadUnsignedByte();
                int modelVerticesX = footer.ReadUnsignedShort();
                int modelVerticesY = footer.ReadUnsignedShort();
                int modelVerticesZ = footer.ReadUnsignedShort();
                int modelVertexPoint = footer.ReadUnsignedShort();
                int modelTextureCoordinates = footer.ReadUnsignedShort();
                int vertices = footer.ReadUnsignedShort();
                int triangles = footer.ReadUnsignedShort();
                if(!hasManyVertices) {
                    if(modelSkins == 1)
                        vertices = numVertices;
                    else
                        vertices = 0;
                }
                if(!hasManyTriangles) {
                    if(modelSkinValue == 1)
                        triangles = numTriangles;
                    else
                        triangles = 0;
                }
                int textureAmount = 0;
                int particleAmount = 0;
                int particleColor = 0;
                if(numTextureTriangles > 0) {
                    textureRenderTypes = new sbyte[numTextureTriangles];
                    footer.Position = 3;
                    for(int triangleTextureIndex = 0; triangleTextureIndex < numTextureTriangles; triangleTextureIndex++) {
                        sbyte type = (textureRenderTypes[triangleTextureIndex] = (sbyte)footer
                                .ReadByte());
                        if(type == 0)
                            textureAmount++;
                        if(type >= 1 && type <= 3)
                            particleAmount++;
                        if(type == 2)
                            particleColor++;
                    }
                }
                int pos = 3 + numTextureTriangles;// now it adds 3 to starting pos
                int vertexModOffset = pos;
                pos += numVertices;
                int triangleDrawTypeBasePos = pos;
                if(hasFillAttributes)
                    pos += numTriangles;
                int triMeshLinkOffset = pos;
                pos += numTriangles;
                int facePriorityBasePos = pos;
                if(modelPriority == 255)
                    pos += numTriangles;
                int triangleBasePos = pos;
                pos += triangles;
                int verticesBasePos = pos;
                pos += vertices;
                int alphaBasePos = pos;
                if(modelAlpha == 1)
                    pos += numTriangles;
                int triangleVertexPointOffset = pos;
                pos += modelVertexPoint;
                int textureBasePos = pos;
                if(modelTexture == 1)
                    pos += numTriangles * 2;
                int textureCoordBasePos = pos;
                pos += modelTextureCoordinates;
                int triangleColourOrTextureBasePos = pos;
                pos += numTriangles * 2;
                int vertexXPos = pos;
                pos += modelVerticesX;
                int vertexYPos = pos;
                pos += modelVerticesY;
                int vertexZPos = pos;
                pos += modelVerticesZ;
                int textureInfoBasePos = pos;
                pos += textureAmount * 6;
                int particleInfoBasePos = pos;
                pos += particleAmount * 6;
                int modelVersionType = 6;
                if(version == 14)
                    modelVersionType = 7;
                else if(version >= 15)
                    modelVersionType = 9;
                int i_93_ = pos;
                pos += particleAmount * modelVersionType;
                int i_94_ = pos;
                pos += particleAmount;
                int i_95_ = pos;
                pos += particleAmount;
                int i_96_ = pos;
                pos += particleAmount + particleColor * 2;
                int footerPosition = pos;
                verticesX = new int[numVertices];
                verticesY = new int[numVertices];
                verticesZ = new int[numVertices];
                triangleViewspaceX = new short[numTriangles];
                triangleViewspaceY = new short[numTriangles];
                triangleViewspaceZ = new short[numTriangles];
                if(modelSkins == 1)
                    vertexSkins = new int[numVertices];
                if(hasFillAttributes)
                    faceRenderType = new byte[numTriangles];
                if(modelPriority == 255)
                    trianglePriorities = new sbyte[numTriangles];
                else
                    priority = (sbyte) modelPriority;
                if(modelAlpha == 1)
                    faceAlpha = new sbyte[numTriangles];
                if(modelSkinValue == 1)
                    triangleSkinValues = new int[numTriangles];
                if(modelTexture == 1)
                    faceTexture = new short[numTriangles];
                if(modelTexture == 1 && numTextureTriangles > 0)
                    textureCoords = new short[numTriangles];
                colorValues = new short[numTriangles];
                if(numTextureTriangles > 0) {
                    textureTrianglePIndex = new ushort[numTextureTriangles];
                    textureTriangleMIndex = new ushort[numTextureTriangles];
                    textureTriangleNIndex = new ushort[numTextureTriangles];
                    particleAmount = 1003;// TODO cheaphaxed because paricle amount
                                          // was never set and caused errors
                    if(particleAmount > 0) {
                        particleDirectionX = new int[particleAmount];
                        particleDirectionY = new int[particleAmount];
                        particleDirectionZ = new int[particleAmount];
                        particleLifespanX = new sbyte[particleAmount];
                        particleLifespanY = new sbyte[particleAmount];
                        particleLifespanZ = new int[particleAmount];
                    }
                    particleColor = 1003;// TODO cheaphaxed because paricle amount
                                         // was never set and caused errors
                    if(particleColor > 0) {
                        texturePrimaryColor = new int[particleColor];
                        textureSecondaryColor = new int[particleColor];
                    }
                }
                footer.Position = vertexModOffset;
                drawTypes.Position = vertexXPos;
                priorities.Position = vertexYPos;
                alphas.Position = vertexZPos;
                trianglesAndVertices.Position = verticesBasePos;
                int vertexX = 0;
                int vertexY = 0;
                int vertexZ = 0;
                for(int point = 0; point < numVertices; point++) {
                    int vertexFlags = footer.ReadUnsignedByte();
                    int vertexXOffset = 0;
                    if((vertexFlags & 0x1) != 0)
                        vertexXOffset = drawTypes.ReadUnsignedSmart();
                    int vertexYOffset = 0;
                    if((vertexFlags & 0x2) != 0)
                        vertexYOffset = priorities.ReadUnsignedSmart();
                    int vertexZOffset = 0;
                    if((vertexFlags & 0x4) != 0)
                        vertexZOffset = alphas.ReadUnsignedSmart();
                    verticesX[point] = vertexX + vertexXOffset;
                    verticesY[point] = vertexY + vertexYOffset;
                    verticesZ[point] = vertexZ + vertexZOffset;
                    vertexX = verticesX[point];
                    vertexY = verticesY[point];
                    vertexZ = verticesZ[point];
                    if(modelSkins == 1) {
                        if(hasManyVertices)
                            vertexSkins[point] = trianglesAndVertices.ReadSpecialSmart();// THIS
                                                                                         // read
                                                                                         // method
                                                                                         // might
                                                                                         // be
                                                                                         // different
                        else {
                            vertexSkins[point] = trianglesAndVertices
                                    .ReadUnsignedByte();
                            if(vertexSkins[point] == 255)
                                vertexSkins[point] = -1;
                        }
                    }
                }
                footer.Position = triangleColourOrTextureBasePos;
                drawTypes.Position = triangleDrawTypeBasePos;
                priorities.Position = facePriorityBasePos;
                alphas.Position = alphaBasePos;
                trianglesAndVertices.Position = triangleBasePos;
                textures.Position = textureBasePos;
                textureCoordinates.Position = textureCoordBasePos;
                for(int i_106_ = 0; i_106_ < numTriangles; i_106_++) {
                    colorValues[i_106_] = (short) footer.ReadUnsignedShort();
                    if(hasFillAttributes)
                        faceRenderType[i_106_] = (byte) drawTypes.ReadByte();
                    if(modelPriority == 255)
                        trianglePriorities[i_106_] = (sbyte) priorities.ReadByte();
                    if(modelAlpha == 1)
                        faceAlpha[i_106_] = (sbyte) alphas.ReadByte();
                    if(modelSkinValue == 1) {
                        if(hasManyTriangles)
                            triangleSkinValues[i_106_] = trianglesAndVertices
                                    .ReadSpecialSmart();
                        else {
                            triangleSkinValues[i_106_] = trianglesAndVertices
                                    .ReadUnsignedByte();
                            if(triangleSkinValues[i_106_] == 255)
                                triangleSkinValues[i_106_] = -1;
                        }
                    }
                    if(modelTexture == 1)
                        faceTexture[i_106_] = (short) (textures.ReadUnsignedShort() - 1);
                    if(textureCoords != null) {
                        if(faceTexture[i_106_] != -1) {
                            if(version >= 16)
                                textureCoords[i_106_] = (short) (textureCoordinates
                                        .ReadSmart() - 1);
                            else
                                textureCoords[i_106_] = (short) (textureCoordinates
                                        .ReadUnsignedByte() - 1);
                        } else
                            textureCoords[i_106_] = (short) -1;
                    }
                }
                maxDepth = -1;
                footer.Position = triangleVertexPointOffset;
                drawTypes.Position = triMeshLinkOffset;
                CalculateMaxDepth(footer, drawTypes, null);
                footer.Position = textureInfoBasePos;
                drawTypes.Position = particleInfoBasePos;
                priorities.Position = i_93_;
                alphas.Position = i_94_;
                trianglesAndVertices.Position = i_95_;
                textures.Position = i_96_;
                DecodeTexturedTriangles(footer, drawTypes, priorities, alphas,
                        trianglesAndVertices, textures);
                footer.Position = footerPosition;
                if(useEffects) {
                    if(hasSurfaceEffects) {
                        int faceAmount = footer.ReadUnsignedByte();
                        if(faceAmount > 0) {
                            surfaces = new Surface[faceAmount];
                            for(int face = 0; face < faceAmount; face++) {
                                int faceId = footer.ReadUnsignedShort();
                                int point = footer.ReadUnsignedShort();
                                sbyte pri;
                                if(modelPriority == 255)
                                    pri = trianglePriorities[point];
                                else
                                    pri = (sbyte) modelPriority;
                                surfaces[face] = new Surface(faceId, point,
                                        triangleViewspaceX[point],
                                        triangleViewspaceY[point],
                                        triangleViewspaceZ[point], (byte) pri);
                            }
                        }
                        int skinAmount = footer.ReadUnsignedByte();
                        if(skinAmount > 0) {
                            surfaceSkins = new SurfaceSkin[skinAmount];
                            for(int face = 0; face < skinAmount; face++) {
                                int skin = footer.ReadUnsignedShort();
                                int point = footer.ReadUnsignedShort();
                                surfaceSkins[face] = new SurfaceSkin(skin, point);
                            }
                        }
                    }
                    if(hasVertexNormals) {
                        int vertexNormalAmount = footer.ReadUnsignedByte();
                        if(vertexNormalAmount > 0) {
                            isolatedVertexNormals = new VertexNormal[vertexNormalAmount];
                            for(int vertex = 0; vertex < vertexNormalAmount; vertex++) {
                                int x = footer.ReadUnsignedShort();
                                int y = footer.ReadUnsignedShort();
                                int z;
                                if(hasManyVertexNormals)
                                    z = footer.ReadSpecialSmart();
                                else {
                                    z = footer.ReadUnsignedByte();
                                    if(z == 255)
                                        z = -1;
                                }
                                sbyte divisor = (sbyte)footer.ReadByte();
                                isolatedVertexNormals[vertex] = new VertexNormal(x,
                                        y, z, (byte) divisor);
                            }
                        }
                    }

                }
            }
        }

        void DecodeTexturedTriangles(JagStream class219_sub41,
                JagStream class219_sub41_244_,
                JagStream class219_sub41_245_,
                JagStream class219_sub41_246_,
                JagStream class219_sub41_247_,
                JagStream class219_sub41_248_) {
            for(int i = 0; i < numTextureTriangles; i++) {
                int type = textureRenderTypes[i] & 0xff;
                if(type == 0) {
                    textureTrianglePIndex[i] = (ushort) class219_sub41
                            .ReadUnsignedShort();
                    textureTriangleMIndex[i] = (ushort) class219_sub41
                            .ReadUnsignedShort();
                    textureTriangleNIndex[i] = (ushort) class219_sub41
                            .ReadUnsignedShort();
                }
                if(type == 1) {
                    textureTrianglePIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    textureTriangleMIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    textureTriangleNIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    if(version < 15) {
                        particleDirectionX[i] = class219_sub41_245_
                                .ReadUnsignedShort();
                        if(version < 14)
                            particleDirectionY[i] = class219_sub41_245_
                                    .ReadUnsignedShort();
                        else
                            particleDirectionY[i] = class219_sub41_245_
                                    .ReadMedium();
                        particleDirectionZ[i] = class219_sub41_245_
                                .ReadUnsignedShort();
                    } else {
                        particleDirectionX[i] = class219_sub41_245_.ReadMedium();
                        particleDirectionY[i] = class219_sub41_245_.ReadMedium();
                        particleDirectionZ[i] = class219_sub41_245_.ReadMedium();
                    }
                    particleLifespanX[i] = (sbyte) class219_sub41_246_.ReadByte();
                    particleLifespanY[i] = (sbyte) class219_sub41_247_.ReadByte();
                    particleLifespanZ[i] = class219_sub41_248_.ReadByte();
                }
                if(type == 2) {
                    textureTrianglePIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    textureTriangleMIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    textureTriangleNIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    if(version < 15) {
                        particleDirectionX[i] = class219_sub41_245_
                                .ReadUnsignedShort();
                        if(version < 14)
                            particleDirectionY[i] = class219_sub41_245_
                                    .ReadUnsignedShort();
                        else
                            particleDirectionY[i] = class219_sub41_245_
                                    .ReadMedium();
                        particleDirectionZ[i] = class219_sub41_245_
                                .ReadUnsignedShort();
                    } else {
                        particleDirectionX[i] = class219_sub41_245_.ReadMedium();
                        particleDirectionY[i] = class219_sub41_245_.ReadMedium();
                        particleDirectionZ[i] = class219_sub41_245_.ReadMedium();
                    }
                    particleLifespanX[i] = (sbyte) class219_sub41_246_.ReadByte();
                    particleLifespanY[i] = (sbyte) class219_sub41_247_.ReadByte();
                    particleLifespanZ[i] = class219_sub41_248_.ReadByte();
                    texturePrimaryColor[i] = class219_sub41_248_.ReadByte();
                    textureSecondaryColor[i] = class219_sub41_248_.ReadByte();
                }
                if(type == 3) {
                    textureTrianglePIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    textureTriangleMIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    textureTriangleNIndex[i] = (ushort) class219_sub41_244_
                            .ReadUnsignedShort();
                    if(version < 15) {
                        particleDirectionX[i] = class219_sub41_245_
                                .ReadUnsignedShort();
                        if(version < 14)
                            particleDirectionY[i] = class219_sub41_245_
                                    .ReadUnsignedShort();
                        else
                            particleDirectionY[i] = class219_sub41_245_
                                    .ReadMedium();
                        particleDirectionZ[i] = class219_sub41_245_
                                .ReadUnsignedShort();
                    } else {
                        particleDirectionX[i] = class219_sub41_245_.ReadMedium();
                        particleDirectionY[i] = class219_sub41_245_.ReadMedium();
                        particleDirectionZ[i] = class219_sub41_245_.ReadMedium();
                    }
                    particleLifespanX[i] = (sbyte) class219_sub41_246_.ReadByte();
                    particleLifespanY[i] = (sbyte) class219_sub41_247_.ReadByte();
                    particleLifespanZ[i] = class219_sub41_248_.ReadByte();
                }
            }
        }

        void CalculateMaxDepth(JagStream class219_sub41,
                JagStream class219_sub41_122_, JagStream var3) {
            short functionX = 0;
            short functionY = 0;
            short functionZ = 0;
            int previousZView = 0;
            for(int tri = 0; tri < numTriangles; tri++) {
                int i_127_ = class219_sub41_122_.ReadUnsignedByte();
                if(i_127_ == 1) {
                    functionX = (short) (class219_sub41.ReadUnsignedSmart() + previousZView);
                    previousZView = functionX;
                    functionY = (short) (class219_sub41.ReadUnsignedSmart() + previousZView);
                    previousZView = functionY;
                    functionZ = (short) (class219_sub41.ReadUnsignedSmart() + previousZView);
                    previousZView = functionZ;
                    triangleViewspaceX[tri] = functionX;
                    triangleViewspaceY[tri] = functionY;
                    triangleViewspaceZ[tri] = functionZ;
                    if(functionX > maxDepth)
                        maxDepth = functionX;
                    if(functionY > maxDepth)
                        maxDepth = functionY;
                    if(functionZ > maxDepth)
                        maxDepth = functionZ;
                }
                if(i_127_ == 2) {
                    functionY = functionZ;
                    functionZ = (short) (class219_sub41.ReadUnsignedSmart() + previousZView);
                    previousZView = functionZ;
                    triangleViewspaceX[tri] = functionX;
                    triangleViewspaceY[tri] = functionY;
                    triangleViewspaceZ[tri] = functionZ;
                    if(functionZ > maxDepth)
                        maxDepth = functionZ;
                }
                if(i_127_ == 3) {
                    functionX = functionZ;
                    functionZ = (short) (class219_sub41.ReadUnsignedSmart() + previousZView);
                    previousZView = functionZ;
                    triangleViewspaceX[tri] = functionX;
                    triangleViewspaceY[tri] = functionY;
                    triangleViewspaceZ[tri] = functionZ;
                    if(functionZ > maxDepth)
                        maxDepth = functionZ;
                }
                if(i_127_ == 4) {
                    short i_128_ = functionX;
                    functionX = functionY;
                    functionY = i_128_;
                    functionZ = (short) (class219_sub41.ReadUnsignedSmart() + previousZView);
                    previousZView = functionZ;
                    triangleViewspaceX[tri] = functionX;
                    triangleViewspaceY[tri] = functionY;
                    triangleViewspaceZ[tri] = functionZ;
                    if(functionZ > maxDepth)
                        maxDepth = functionZ;
                }
                if(this.anInt1234 > 0 && (i_127_ & 8) != 0) {
                    this.aByteArray1241[tri] = (byte) var3.ReadUnsignedByte();
                    this.aByteArray1266[tri] = (byte) var3.ReadUnsignedByte();
                    this.aByteArray1243[tri] = (byte) var3.ReadUnsignedByte();
                }
            }
            maxDepth++;
        }

        void DecodeNewOldModel(sbyte[] instream) {
            bool bool1 = false;
            bool bool_136_ = false;
            JagStream footer = new JagStream((byte[])(Array)instream);
            JagStream class219_sub41_137_ = new JagStream((byte[])(Array)instream);
            JagStream class219_sub41_138_ = new JagStream((byte[])(Array)instream);
            JagStream class219_sub41_139_ = new JagStream((byte[])(Array)instream);
            JagStream class219_sub41_140_ = new JagStream((byte[])(Array)instream);
            int i = footer.ReadUnsignedByte();
            if(i != 0)
                Console.WriteLine(i);
            else {
                footer.Position = (instream.Length - 18);
                numVertices = footer.ReadUnsignedShort();
                numTriangles = footer.ReadUnsignedShort();
                numTextureTriangles = footer.ReadUnsignedByte();
                int useTexturesFlag = footer.ReadUnsignedByte();
                int useTrianglePrioritiesFlag = footer.ReadUnsignedByte();
                int useTransparencyFlag = footer.ReadUnsignedByte();
                int useTriangleSkinningFlag = footer.ReadUnsignedByte();
                int useVertexSkinningFlag = footer.ReadUnsignedByte();
                int xDataLength = footer.ReadUnsignedShort();
                int yDataLength = footer.ReadUnsignedShort();
                int zDataLength = footer.ReadUnsignedShort();
                int triangleDataLength = footer.ReadUnsignedShort();
                int i_150_ = 0;
                int i_151_ = i_150_;
                i_150_ += numVertices;
                int i_152_ = i_150_;
                i_150_ += numTriangles;
                int i_153_ = i_150_;
                if(useTrianglePrioritiesFlag == 255)
                    i_150_ += numTriangles;
                int i_154_ = i_150_;
                if(useTriangleSkinningFlag == 1)
                    i_150_ += numTriangles;
                int i_155_ = i_150_;
                if(useTexturesFlag == 1)
                    i_150_ += numTriangles;
                int i_156_ = i_150_;
                if(useVertexSkinningFlag == 1)
                    i_150_ += numVertices;
                int i_157_ = i_150_;
                if(useTransparencyFlag == 1)
                    i_150_ += numTriangles;
                int i_158_ = i_150_;
                i_150_ += triangleDataLength;
                int i_159_ = i_150_;
                i_150_ += numTriangles * 2;
                int i_160_ = i_150_;
                i_150_ += numTextureTriangles * 6;
                int i_161_ = i_150_;
                i_150_ += xDataLength;
                int i_162_ = i_150_;
                i_150_ += yDataLength;
                int i_163_ = i_150_;
                i_150_ += zDataLength;
                verticesX = new int[numVertices];
                verticesY = new int[numVertices];
                verticesZ = new int[numVertices];
                triangleViewspaceX = new short[numTriangles];
                triangleViewspaceY = new short[numTriangles];
                triangleViewspaceZ = new short[numTriangles];
                if(numTextureTriangles > 0) {
                    textureRenderTypes = new sbyte[numTextureTriangles];
                    textureTrianglePIndex = new ushort[numTextureTriangles];
                    textureTriangleMIndex = new ushort[numTextureTriangles];
                    textureTriangleNIndex = new ushort[numTextureTriangles];
                }
                if(useVertexSkinningFlag == 1)
                    vertexSkins = new int[numVertices];
                if(useTexturesFlag == 1) {
                    faceRenderType = new byte[numTriangles];
                    textureCoords = new short[numTriangles];
                    faceTexture = new short[numTriangles];
                }
                if(useTrianglePrioritiesFlag == 255)
                    trianglePriorities = new sbyte[numTriangles];
                else
                    priority = (sbyte) useTrianglePrioritiesFlag;
                if(useTransparencyFlag == 1)
                    faceAlpha = new sbyte[numTriangles];
                if(useTriangleSkinningFlag == 1)
                    triangleSkinValues = new int[numTriangles];
                colorValues = new short[numTriangles];
                footer.Position = i_151_;
                class219_sub41_137_.Position = i_161_;
                class219_sub41_138_.Position = i_162_;
                class219_sub41_139_.Position = i_163_;
                class219_sub41_140_.Position = i_156_;
                int i_164_ = 0;
                int i_165_ = 0;
                int i_166_ = 0;
                for(int i_167_ = 0; i_167_ < numVertices; i_167_++) {
                    int i_168_ = footer.ReadUnsignedByte();
                    int i_169_ = 0;
                    if((i_168_ & 0x1) != 0)
                        i_169_ = class219_sub41_137_.ReadUnsignedSmart();
                    int i_170_ = 0;
                    if((i_168_ & 0x2) != 0)
                        i_170_ = class219_sub41_138_.ReadUnsignedSmart();
                    int i_171_ = 0;
                    if((i_168_ & 0x4) != 0)
                        i_171_ = class219_sub41_139_.ReadUnsignedSmart();
                    verticesX[i_167_] = i_164_ + i_169_;
                    verticesY[i_167_] = i_165_ + i_170_;
                    verticesZ[i_167_] = i_166_ + i_171_;
                    i_164_ = verticesX[i_167_];
                    i_165_ = verticesY[i_167_];
                    i_166_ = verticesZ[i_167_];
                    if(useVertexSkinningFlag == 1)
                        vertexSkins[i_167_] = class219_sub41_140_.ReadUnsignedByte();
                }
                footer.Position = i_159_;
                class219_sub41_137_.Position = i_155_;
                class219_sub41_138_.Position = i_153_;
                class219_sub41_139_.Position = i_157_;
                class219_sub41_140_.Position = i_154_;
                for(int i_172_ = 0; i_172_ < numTriangles; i_172_++) {
                    colorValues[i_172_] = (short) footer.ReadUnsignedShort();
                    if(useTexturesFlag == 1) {
                        int i_173_ = class219_sub41_137_.ReadUnsignedByte();
                        if((i_173_ & 0x1) == 1) {
                            faceRenderType[i_172_] = (byte) 1;
                            bool1 = true;
                        } else
                            faceRenderType[i_172_] = (byte) 0;
                        if((i_173_ & 0x2) == 2) {
                            textureCoords[i_172_] = (short) (sbyte) (i_173_ >> 2);
                            faceTexture[i_172_] = colorValues[i_172_];
                            colorValues[i_172_] = (short) 127;
                            if(faceTexture[i_172_] != -1)
                                bool_136_ = true;
                        } else {
                            textureCoords[i_172_] = (short) -1;
                            faceTexture[i_172_] = (short) -1;
                        }
                    }
                    if(useTrianglePrioritiesFlag == 255)
                        trianglePriorities[i_172_] = (sbyte) class219_sub41_138_
                                .ReadByte();
                    if(useTransparencyFlag == 1)
                        faceAlpha[i_172_] = (sbyte) class219_sub41_139_.ReadByte();
                    if(useTriangleSkinningFlag == 1)
                        triangleSkinValues[i_172_] = class219_sub41_140_
                                .ReadUnsignedByte();
                }
                maxDepth = -1;
                footer.Position = i_158_;
                class219_sub41_137_.Position = i_152_;
                short i_174_ = 0;
                short i_175_ = 0;
                short i_176_ = 0;
                int i_177_ = 0;
                for(int i_178_ = 0; i_178_ < numTriangles; i_178_++) {
                    int i_179_ = class219_sub41_137_.ReadUnsignedByte();
                    if(i_179_ == 1) {
                        i_174_ = (short) (footer.ReadUnsignedSmart() + i_177_);
                        i_177_ = i_174_;
                        i_175_ = (short) (footer.ReadUnsignedSmart() + i_177_);
                        i_177_ = i_175_;
                        i_176_ = (short) (footer.ReadUnsignedSmart() + i_177_);
                        i_177_ = i_176_;
                        triangleViewspaceX[i_178_] = i_174_;
                        triangleViewspaceY[i_178_] = i_175_;
                        triangleViewspaceZ[i_178_] = i_176_;
                        if(i_174_ > maxDepth)
                            maxDepth = i_174_;
                        if(i_175_ > maxDepth)
                            maxDepth = i_175_;
                        if(i_176_ > maxDepth)
                            maxDepth = i_176_;
                    }
                    if(i_179_ == 2) {
                        i_175_ = i_176_;
                        i_176_ = (short) (footer.ReadUnsignedSmart() + i_177_);
                        i_177_ = i_176_;
                        triangleViewspaceX[i_178_] = i_174_;
                        triangleViewspaceY[i_178_] = i_175_;
                        triangleViewspaceZ[i_178_] = i_176_;
                        if(i_176_ > maxDepth)
                            maxDepth = i_176_;
                    }
                    if(i_179_ == 3) {
                        i_174_ = i_176_;
                        i_176_ = (short) (footer.ReadUnsignedSmart() + i_177_);
                        i_177_ = i_176_;
                        triangleViewspaceX[i_178_] = i_174_;
                        triangleViewspaceY[i_178_] = i_175_;
                        triangleViewspaceZ[i_178_] = i_176_;
                        if(i_176_ > maxDepth)
                            maxDepth = i_176_;
                    }
                    if(i_179_ == 4) {
                        short i_180_ = i_174_;
                        i_174_ = i_175_;
                        i_175_ = i_180_;
                        i_176_ = (short) (footer.ReadUnsignedSmart() + i_177_);
                        i_177_ = i_176_;
                        triangleViewspaceX[i_178_] = i_174_;
                        triangleViewspaceY[i_178_] = i_175_;
                        triangleViewspaceZ[i_178_] = i_176_;
                        if(i_176_ > maxDepth)
                            maxDepth = i_176_;
                    }
                }
                maxDepth++;
                footer.Position = i_160_;
                for(int i_181_ = 0; i_181_ < numTextureTriangles; i_181_++) {
                    textureRenderTypes[i_181_] = (sbyte) 0;
                    textureTrianglePIndex[i_181_] = (ushort) footer
                            .ReadUnsignedShort();
                    textureTriangleMIndex[i_181_] = (ushort) footer
                            .ReadUnsignedShort();
                    textureTriangleNIndex[i_181_] = (ushort) footer
                            .ReadUnsignedShort();
                }
                if(textureCoords != null) {
                    bool bool_182_ = false;
                    for(int i_183_ = 0; i_183_ < numTriangles; i_183_++) {
                        int i_184_ = textureCoords[i_183_] & 0xff;
                        if(i_184_ != 255) {
                            if(((textureTrianglePIndex[i_184_] & 0xffff) == triangleViewspaceX[i_183_])
                                    && ((textureTriangleMIndex[i_184_] & 0xffff) == triangleViewspaceY[i_183_])
                                    && ((textureTriangleNIndex[i_184_] & 0xffff) == triangleViewspaceZ[i_183_]))
                                textureCoords[i_183_] = (short) -1;
                            else
                                bool_182_ = true;
                        }
                    }
                    if(!bool_182_)
                        textureCoords = null;
                }
                if(!bool_136_)
                    faceTexture = null;
                if(!bool1)
                    faceRenderType = null;
            }
        }


        void Read800Model(sbyte[] data) {
            JagStream header = new JagStream((byte[])(Array)data);
            JagStream drawTypes = new JagStream((byte[])(Array)data);
            JagStream priorities = new JagStream((byte[])(Array)data);
            JagStream alphas = new JagStream((byte[])(Array)data);
            JagStream var6 = new JagStream((byte[])(Array)data);
            JagStream var7 = new JagStream((byte[])(Array)data);
            JagStream var8 = new JagStream((byte[])(Array)data);
            int modelType = header.ReadUnsignedByte();
            if(modelType != 1) {
                Console.WriteLine("" + modelType);
            } else {
                header.ReadUnsignedByte();
                this.version = header.ReadUnsignedByte();
                header.Position = data.Length - 26;// -1814364954;
                this.numVertices = header.ReadUnsignedShort();
                this.numTriangles = header.ReadUnsignedShort();
                this.numTextureTriangles = header.ReadUnsignedShort();
                int headerFlags = header.ReadUnsignedByte();
                bool hasFillAttributes = (headerFlags & 1) == 1;
                bool hasSurfaceEffects = (headerFlags & 2) == 2;
                bool hasVertexNormals = (headerFlags & 4) == 4;
                bool hasManyVertices = (headerFlags & 16) == 16;
                bool hasManyTriangles = (headerFlags & 32) == 32;
                bool hasManyVertexNormals = (headerFlags & 64) == 64;
                bool hasUVCoordinates = (headerFlags & 128) == 128;
                int var18 = header.ReadUnsignedByte();
                int var19 = header.ReadUnsignedByte();
                int modelSkinValue = header.ReadUnsignedByte();
                int var21 = header.ReadUnsignedByte();
                int modelSkins = header.ReadUnsignedByte();
                int var23 = header.ReadUnsignedShort();
                int var24 = header.ReadUnsignedShort();
                int var25 = header.ReadUnsignedShort();
                int var26 = header.ReadUnsignedShort();
                int var27 = header.ReadUnsignedShort();
                int vertices = header.ReadUnsignedShort();
                int triangles = header.ReadUnsignedShort();
                if(!hasManyVertices) {
                    if(modelSkins == 1) {
                        vertices = this.numVertices;
                    } else {
                        vertices = 0;
                    }
                }

                if(!hasManyTriangles) {
                    if(modelSkinValue == 1) {
                        triangles = this.numTriangles;
                    } else {
                        triangles = 0;
                    }
                }

                int textureAmount = 0;
                int particleAmount = 0;
                int particleColor = 0;
                int triangleTextureIndex;
                if(this.numTextureTriangles > 0) {
                    this.textureRenderTypes = new sbyte[this.numTextureTriangles];
                    header.Position = 3;

                    for(triangleTextureIndex = 0; triangleTextureIndex < this.numTextureTriangles; ++triangleTextureIndex) {
                        sbyte type = this.textureRenderTypes[triangleTextureIndex] = (sbyte)header.ReadByte();
                        if(type == 0) {
                            ++textureAmount;
                        }

                        if(type >= 1 && type <= 3) {
                            ++particleAmount;
                        }

                        if(type == 2) {
                            ++particleColor;
                        }
                    }
                }

                triangleTextureIndex = 3 + this.numTextureTriangles;
                int var69 = triangleTextureIndex;
                triangleTextureIndex += this.numVertices;
                int var35 = triangleTextureIndex;
                if(hasFillAttributes) {
                    triangleTextureIndex += this.numTriangles;
                }

                int var36 = triangleTextureIndex;
                triangleTextureIndex += this.numTriangles;
                int var37 = triangleTextureIndex;
                if(var18 == 255) {
                    triangleTextureIndex += this.numTriangles;
                }

                int var38 = triangleTextureIndex;
                triangleTextureIndex += triangles;
                int var39 = triangleTextureIndex;
                triangleTextureIndex += vertices;
                int var40 = triangleTextureIndex;
                if(var19 == 1) {
                    triangleTextureIndex += this.numTriangles;
                }

                int var41 = triangleTextureIndex;
                triangleTextureIndex += var26;
                int var42 = triangleTextureIndex;
                if(var21 == 1) {
                    triangleTextureIndex += this.numTriangles * 2;
                }

                int var43 = triangleTextureIndex;
                triangleTextureIndex += var27;
                int var44 = triangleTextureIndex;
                triangleTextureIndex += this.numTriangles * 2;
                int var45 = triangleTextureIndex;
                triangleTextureIndex += var23;
                int var46 = triangleTextureIndex;
                triangleTextureIndex += var24;
                int var47 = triangleTextureIndex;
                triangleTextureIndex += var25;
                int var48 = triangleTextureIndex;
                triangleTextureIndex += textureAmount * 6;
                int var49 = triangleTextureIndex;
                triangleTextureIndex += particleAmount * 6;
                byte var50 = 6;
                if(this.version == 14) {
                    var50 = 7;
                } else if(this.version >= 15) {
                    var50 = 9;
                }

                int var51 = triangleTextureIndex;
                triangleTextureIndex += particleAmount * var50;
                int var52 = triangleTextureIndex;
                triangleTextureIndex += particleAmount;
                int var53 = triangleTextureIndex;
                triangleTextureIndex += particleAmount;
                int var54 = triangleTextureIndex;
                triangleTextureIndex += particleAmount + particleColor * 2;
                int var56 = data.Length;
                int var57 = data.Length;
                int var58 = data.Length;
                int var59 = data.Length;
                int var61;
                int var62;
                if(hasUVCoordinates) {
                    JagStream var60 = new JagStream((byte[])(Array)data);
                    var60.Position = data.Length - 26;//-1814364954;
                    var60.Position -= data[var60.Position - 1];// * 1582127231 - 1582127231];
                    this.anInt1234 = var60.ReadUnsignedShort();
                    var61 = var60.ReadUnsignedShort();
                    var62 = var60.ReadUnsignedShort();
                    var56 = triangleTextureIndex + var61;
                    var57 = var56 + var62;
                    var58 = var57 + this.numVertices;
                    var59 = var58 + this.anInt1234 * 2;
                }

                this.verticesX = new int[this.numVertices];
                this.verticesY = new int[this.numVertices];
                this.verticesZ = new int[this.numVertices];
                this.triangleViewspaceX = new short[this.numTriangles];
                this.triangleViewspaceY = new short[this.numTriangles];
                this.triangleViewspaceZ = new short[this.numTriangles];
                if(modelSkins == 1) {
                    this.vertexSkins = new int[this.numVertices];
                }

                if(hasFillAttributes) {
                    this.faceRenderType = new byte[this.numTriangles];
                }

                if(var18 == 255) {
                    this.trianglePriorities = new sbyte[this.numTriangles];
                } else {
                    this.priority = (sbyte) var18;
                }

                if(var19 == 1) {
                    this.faceAlpha = new sbyte[this.numTriangles];
                }

                if(modelSkinValue == 1) {
                    this.triangleSkinValues = new int[this.numTriangles];
                }

                if(var21 == 1) {
                    this.faceTexture = new short[this.numTriangles];
                }

                if(var21 == 1 && (this.numTextureTriangles > 0 || this.anInt1234 > 0)) {
                    this.textureCoords = new short[this.numTriangles];
                }

                this.colorValues = new short[this.numTriangles];
                if(this.numTextureTriangles > 0) {
                    this.textureTrianglePIndex = new ushort[this.numTextureTriangles];
                    this.textureTriangleMIndex = new ushort[this.numTextureTriangles];
                    this.textureTriangleNIndex = new ushort[this.numTextureTriangles];
                    if(particleAmount > 0) {
                        this.particleDirectionX = new int[particleAmount];
                        this.particleDirectionY = new int[particleAmount];
                        this.particleDirectionZ = new int[particleAmount];
                        this.particleLifespanX = new sbyte[particleAmount];
                        this.particleLifespanY = new sbyte[particleAmount];
                        this.particleLifespanZ = new int[particleAmount];
                    }

                    if(particleColor > 0) {
                        this.texturePrimaryColor = new int[particleColor];
                        this.textureSecondaryColor = new int[particleColor];
                    }
                }

                header.Position = var69;
                drawTypes.Position = var45;
                priorities.Position = var46;
                alphas.Position = var47;
                var6.Position = var39;
                int var70 = 0;
                var61 = 0;
                var62 = 0;

                int point;
                int vertexFlags;
                int vertexXOffset;
                int vertexYOffset;
                int vertexZOffset;
                for(point = 0; point < this.numVertices; ++point) {
                    vertexFlags = header.ReadUnsignedByte();
                    vertexXOffset = 0;
                    if((vertexFlags & 1) != 0) {
                        vertexXOffset = drawTypes.ReadUnsignedSmart();
                    }

                    vertexYOffset = 0;
                    if((vertexFlags & 2) != 0) {
                        vertexYOffset = priorities.ReadUnsignedSmart();
                    }

                    vertexZOffset = 0;
                    if((vertexFlags & 4) != 0) {
                        vertexZOffset = alphas.ReadUnsignedSmart();
                    }

                    this.verticesX[point] = var70 + vertexXOffset;
                    this.verticesY[point] = var61 + vertexYOffset;
                    this.verticesZ[point] = var62 + vertexZOffset;
                    var70 = this.verticesX[point];
                    var61 = this.verticesY[point];
                    var62 = this.verticesZ[point];
                    if(modelSkins == 1) {
                        if(hasManyVertices) {
                            this.vertexSkins[point] = var6.ReadSpecialSmart();
                        } else {
                            this.vertexSkins[point] = var6.ReadUnsignedByte();
                            if(this.vertexSkins[point] == 255) {
                                this.vertexSkins[point] = -1;
                            }
                        }
                    }
                }

                if(this.anInt1234 > 0) {
                    header.Position = var57;
                    drawTypes.Position = var58;
                    priorities.Position = var59;
                    this.anIntArray1231 = new int[this.numVertices];
                    point = 0;

                    for(vertexFlags = 0; point < this.numVertices; ++point) {
                        this.anIntArray1231[point] = vertexFlags;
                        vertexFlags += header.ReadUnsignedByte();
                    }

                    this.aByteArray1241 = new byte[this.numTriangles];
                    this.aByteArray1266 = new byte[this.numTriangles];
                    this.aByteArray1243 = new byte[this.numTriangles];
                    this.texCoordU = new float[this.anInt1234];
                    this.texCoordV = new float[this.anInt1234];

                    for(point = 0; point < this.anInt1234; ++point) {
                        this.texCoordU[point] = (float) drawTypes.ReadShort() / 4096.0F;
                        this.texCoordV[point] = (float) priorities.ReadShort() / 4096.0F;
                    }
                }

                header.Position = var44;
                drawTypes.Position = var35;
                priorities.Position = var37;
                alphas.Position = var40;
                var6.Position = var38;
                var7.Position = var42;
                var8.Position = var43;

                for(point = 0; point < this.numTriangles; ++point) {
                    this.colorValues[point] = (short) header.ReadUnsignedShort();
                    if(hasFillAttributes) {
                        this.faceRenderType[point] = (byte) drawTypes.ReadByte();
                    }

                    if(var18 == 255) {
                        this.trianglePriorities[point] = (sbyte) priorities.ReadByte();
                    }

                    if(var19 == 1) {
                        this.faceAlpha[point] = (sbyte) alphas.ReadByte();
                    }

                    if(modelSkinValue == 1) {
                        if(hasManyTriangles) {
                            this.triangleSkinValues[point] = var6.ReadSpecialSmart();
                        } else {
                            this.triangleSkinValues[point] = var6.ReadUnsignedByte();
                            if(this.triangleSkinValues[point] == 255) {
                                this.triangleSkinValues[point] = -1;
                            }
                        }
                    }

                    if(var21 == 1) {
                        this.faceTexture[point] = (short) (var7.ReadUnsignedShort() - 1);
                    }

                    if(this.textureCoords != null) {
                        if(this.faceTexture[point] != -1) {
                            if(this.version >= 16) {
                                this.textureCoords[point] = (short) (var8.ReadSmart() - 1);
                            } else {
                                this.textureCoords[point] = (short) (var8.ReadUnsignedByte() - 1);
                            }
                        } else {
                            this.textureCoords[point] = -1;
                        }
                    }
                }

                this.maxDepth = -1;
                header.Position = var41;
                drawTypes.Position = var36;
                priorities.Position = var56;
                this.CalculateMaxDepth(header, drawTypes, priorities);
                header.Position = var48;
                drawTypes.Position = var49;
                priorities.Position = var51;
                alphas.Position = var52;
                var6.Position = var53;
                var7.Position = var54;
                this.DecodeTexturedTriangles(header, drawTypes, priorities, alphas, var6, var7);
                header.Position = triangleTextureIndex;
                if(hasSurfaceEffects) {
                    point = header.ReadUnsignedByte();
                    if(point > 0) {
                        this.surfaces = new Surface[point];

                        for(vertexFlags = 0; vertexFlags < point; ++vertexFlags) {
                            vertexXOffset = header.ReadUnsignedShort();
                            vertexYOffset = header.ReadUnsignedShort();
                            sbyte var71;
                            if(var18 == 255) {
                                var71 = this.trianglePriorities[vertexYOffset];
                            } else {
                                var71 = (sbyte) var18;
                            }

                            this.surfaces[vertexFlags] = new Surface(vertexXOffset, vertexYOffset, this.triangleViewspaceX[vertexYOffset], this.triangleViewspaceY[vertexYOffset], this.triangleViewspaceZ[vertexYOffset], (byte) var71);
                        }
                    }

                    vertexFlags = header.ReadUnsignedByte();
                    if(vertexFlags > 0) {
                        this.surfaceSkins = new SurfaceSkin[vertexFlags];

                        for(vertexXOffset = 0; vertexXOffset < vertexFlags; ++vertexXOffset) {
                            vertexYOffset = header.ReadUnsignedShort();
                            vertexZOffset = header.ReadUnsignedShort();
                            this.surfaceSkins[vertexXOffset] = new SurfaceSkin(vertexYOffset, vertexZOffset);
                        }
                    }
                }

                if(hasVertexNormals) {
                    point = header.ReadUnsignedByte();
                    if(point > 0) {
                        this.isolatedVertexNormals = new VertexNormal[point];

                        for(vertexFlags = 0; vertexFlags < point; ++vertexFlags) {
                            vertexXOffset = header.ReadUnsignedShort();
                            vertexYOffset = header.ReadUnsignedShort();
                            if(hasManyVertexNormals) {
                                vertexZOffset = header.ReadSpecialSmart();
                            } else {
                                vertexZOffset = header.ReadUnsignedByte();
                                if(vertexZOffset == 255) {
                                    vertexZOffset = -1;
                                }
                            }

                            byte var68 = (byte)header.ReadByte();
                            this.isolatedVertexNormals[vertexFlags] = new VertexNormal(vertexXOffset, vertexYOffset, vertexZOffset, var68);
                        }
                    }
                }

            }
        }


        public struct SurfaceSkin {
            public int var1;
            public int var2;

            public SurfaceSkin(int var1, int var2) {
                this.var1 = var1;
                this.var2 = var2;
            }
        }
        public struct VertexNormal {

            public int x;
            public int y;
            public int z;
            public byte divisor;


            public VertexNormal(int var1, int var2, int var3, byte var4) {
                this.x = var1;
                this.y = var2;
                this.z = var3;
                this.divisor = var4;
            }

            public VertexNormal Copy() {
                VertexNormal copy = new VertexNormal();
                copy.x = this.x;
                copy.y = this.y;
                copy.z = this.z;
                copy.divisor = this.divisor;
                return copy;
            }
        }
        public struct Surface {


            public Surface(int faceId, int point, short s, short t, short u,
                    byte priority) {
                this.var1 = faceId;
                this.var2 = point;
                this.var3 = s;
                this.var4 = t;
                this.var5 = u;
                this.var6 = priority;
            }

            public int var1;
            public int var2;
            public short var3;
            public short var4;
            public short var5;
            public byte var6;

            public Surface(int var1, int var2, byte var3) {
                this.var1 = var1;
                this.var2 = var2;
                this.var6 = var3;

                this.var3 = 0;
                this.var4 = 0;
                this.var5 = 0;
            }
        }
    }

}

