using FlashEditor.utils;
using System;

namespace FlashEditor {
    internal class NPCDefinition {
        public String[] op;
        public String name;

        public bool clickable, isVisible, drawMinimapDot, aBool8460, aBool8492, aBool8459, aBook8472;

        public bool unknownBoolean7;
        public int[] cursorOps;
        private int movementType;
        private byte[] aByteArray8445, aByteArray8446;

        public int[][] anIntArrayArray8478, anIntArrayArray882;
        public int[] transforms, recolorDst, retextureDst, meshes, retextureSrc, anIntArray8493, interfaceModelId, recolorSrc;

        public int id, unknownInt13, unknownInt6, unknownInt15, size = 1, anInt8483, transVarBit, anInt8443, renderTypeID, anInt8488,
            degreesToTurn, level, height, ambient, mapIcon, anInt8455, attackOpCursor, unknownInt14, anInt8457, anInt8466,
            headIcon, unknownInt19, anInt8484, transVar, contrast, width, npcId, anInt8465, unknownInt16;

        int aByte821, aByte824, aByte843, aByte855, anInt864, anInt848, anInt837, anInt847, anInt828;

        public short aShort8473, aShort8474;

        public byte[] recolorDstPalette;
        public byte aByte833, aByte8477, aByte8476, walkMask, respawnDirection;


        /// <summary>
        /// Constructs a new item definition from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the encoded item data</param>
        public NPCDefinition(JagStream stream) {
            setDefaults();
            Decode(stream);
        }

        public void setDefaults() {
            name = "null";
            level = -1;
            drawMinimapDot = true;
            renderTypeID = -1;
            respawnDirection = 7;
            size = 1;
            anInt8483 = -1;
            transVarBit = -1;
            unknownInt15 = -1;
            anInt8443 = -1;
            degreesToTurn = 32;
            unknownInt6 = -1;
            ambient = 0;
            walkMask = 0;
            anInt8488 = 255;
            anInt8455 = -1;
            aBool8492 = true;
            aShort8473 = 0;
            anInt8466 = -1;
            aByte8477 = 223;
            anInt8457 = 0;
            attackOpCursor = -1;
            aBook8472 = true;
            mapIcon = -1;
            unknownInt14 = -1;
            unknownInt13 = -1;
            height = 128;
            headIcon = -1;
            aBool8459 = false;
            transVar = -1;
            aByte8476 = 143;
            isVisible = false;
            unknownInt16 = -1;
            anInt8484 = -1;
            clickable = true;
            unknownInt19 = -1;
            width = 128;
            aShort8474 = 0;
            contrast = 0;
            anInt8465 = -1;
        }

        /// <summary>
        /// Overrides defaults from the stream
        /// </summary>
        /// <param name="stream"></param>
        public void Decode(JagStream stream) {
            if(stream != null) {
                while(true) {
                    int opcode = stream.ReadUnsignedByte();

                    if(opcode == 0)
                        break;

                    Decode(stream, opcode);
                }
            }
        }

        /// <summary>
        /// Overrides a specific property corresponding to opcode
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="opcode">The opcode value signalling which type to read</param>
        private void Decode(JagStream stream, int opcode) {
            DebugUtil.Debug("Reading opcode: " + opcode);

            if(opcode == 1) {
                int i = stream.ReadByte();
                meshes = new int[i];
                for(int k = 0; k < i; k++) {
                    meshes[k] = stream.ReadShort();
                    if((meshes[k] ^ 0xffffffff) == -65536)
                        meshes[k] = -1;
                }
            } else if(opcode == 2) {
                name = stream.ReadString();
            } else if(opcode == 12) {
                size = stream.ReadByte();
            } else if(opcode >= 30 && opcode < 36) {
                op[opcode - 30] = stream.ReadString();
                if(op[opcode - 30] == "Hidden")
                    op[opcode - 30] = null;
            } else if(opcode == 40) {
                int i = stream.ReadByte();
                recolorDst = new int[i];
                recolorSrc = new int[i];
                for(int k = 0; (i ^ 0xffffffff) < (k ^ 0xffffffff); k++) {
                    recolorSrc[k] = stream.ReadShort();
                    recolorDst[k] = stream.ReadShort();
                }
            } else if(opcode == 41) {
                int i = stream.ReadByte();
                retextureSrc = new int[i];
                retextureDst = new int[i];
                for(int i_54_ = 0; (i_54_ ^ 0xffffffff) > (i ^ 0xffffffff); i_54_++) {
                    retextureSrc[i_54_] = stream.ReadShort();
                    retextureDst[i_54_] = stream.ReadShort();
                }
            } else if(opcode == 42) {
                int i = stream.ReadByte();
                recolorDstPalette = new byte[i];
                for(int i_55_ = 0; i > i_55_; i_55_++) {
                    recolorDstPalette[i_55_] = (byte) stream.ReadByte();
                }
            } else if(opcode == 44) {
                stream.ReadShort();
            } else if(opcode == 45) {
                stream.ReadShort();
            } else if(opcode == 60) {
                int i = stream.ReadByte();
                interfaceModelId = new int[i];
                for(int i_64_ = 0; (i_64_ ^ 0xffffffff) > (i ^ 0xffffffff); i_64_++)
                    interfaceModelId[i_64_] = stream.ReadShort();
            } else if(opcode == 93) {
                drawMinimapDot = false;
            } else if(opcode == 95) {
                level = stream.ReadShort();
            } else if(opcode == 97) {
                height = stream.ReadShort();
            } else if(opcode == 98) {
                width = stream.ReadShort();
            } else if(opcode == 99) {
                isVisible = true;
            } else if(opcode == 100) {
                ambient = stream.ReadByte();
            } else if(opcode == 101) {
                contrast = stream.ReadByte();
            } else if(opcode == 102) {
                headIcon = stream.ReadShort();
            } else if(opcode == 103) {
                degreesToTurn = stream.ReadShort();
            } else if(opcode == 106 || opcode == 118) {
                transVarBit = stream.ReadShort();
                if(transVarBit == 65535)
                    transVarBit = -1;

                transVar = stream.ReadShort();
                if(transVar == 65535)
                    transVar = -1;

                int defaultType = -1;
                if(opcode == 118) {
                    defaultType = stream.ReadShort();
                    if((defaultType) == 65535)
                        defaultType = -1;
                }

                int tCount = stream.ReadByte();
                transforms = new int[2 + tCount];
                for(int index = 0; tCount >= index; index++) {
                    transforms[index] = stream.ReadShort();
                    if(transforms[index] == 65535)
                        transforms[index] = -1;
                }
                transforms[tCount + 1] = defaultType;
            } else if(opcode == 107)
                clickable = false;
            else if(opcode == 109)
                aBool8492 = false;
            else if(opcode == 111)
                aBook8472 = false;
            else if(opcode == 113) {
                aShort8473 = (short) (stream.ReadShort());
                aShort8474 = (short) (stream.ReadShort());
            } else if(opcode == 114) {
                aByte8477 = (byte) (stream.ReadByte());
                aByte8476 = (byte) (stream.ReadByte());
            } else if(opcode == 119)
                walkMask = (byte) (stream.ReadByte());
            else if(opcode == 121) {
                anIntArrayArray8478 = (new int[meshes.Length][]);
                int i = (stream.ReadByte());
                for(int i_62_ = 0; ((i_62_ ^ 0xffffffff) > (i ^ 0xffffffff)); i_62_++) {
                    int i_63_ = stream.ReadByte();
                    int[] nigga = (anIntArrayArray8478[i_63_] = (new int[3]));
                    nigga[0] = stream.ReadByte();
                    nigga[1] = stream.ReadByte();
                    nigga[2] = stream.ReadByte();
                }
            } else if(opcode == 122)
                unknownInt6 = (stream.ReadShort());
            else if(opcode == 123)
                anInt8443 = (stream.ReadShort());
            else if(opcode == 125)
                respawnDirection = (byte) (stream.ReadByte());
            else if(opcode == 127)
                renderTypeID = (stream.ReadShort());
            else if(opcode == 128)
                movementType = stream.ReadByte();
            else if(opcode == 134) {
                anInt8466 = (stream.ReadShort());
                if(anInt8466 == 65535)
                    anInt8466 = -1;

                anInt8483 = (stream.ReadShort());
                if(anInt8483 == 65535)
                    anInt8483 = -1;

                anInt8484 = (stream.ReadShort());
                if((anInt8484 ^ 0xffffffff) == -65536)
                    anInt8484 = -1;

                anInt8455 = (stream.ReadShort());
                if((anInt8455 ^ 0xffffffff) == -65536)
                    anInt8455 = -1;

                anInt8457 = stream.ReadByte();
            } else if(opcode == 135) {
                unknownInt13 = stream.ReadByte();
                unknownInt14 = stream.ReadShort();
            } else if(opcode == 136) {
                unknownInt15 = stream.ReadByte();
                unknownInt16 = stream.ReadShort();
            } else if(opcode == 137)
                attackOpCursor = stream.ReadShort();
            else if(opcode == 138)
                anInt8465 = stream.ReadShort();
            else if(opcode == 139)
                unknownInt19 = stream.ReadShort();
            else if(opcode == 140)
                anInt8488 = stream.ReadByte();
            else if(opcode == 141)
                aBool8460 = true;
            else if(opcode == 142)
                mapIcon = stream.ReadShort();
            else if(opcode == 143) {
                aBool8459 = true;
            } else if(opcode >= 150 && opcode < 155) {
                op[opcode - 150] = stream.ReadString();
                if(op[opcode - 150] == "Hidden")
                    op[opcode - 150] = null;
            } else if(opcode == 155) {
                aByte821 = stream.ReadByte();
                aByte824 = stream.ReadByte();
                aByte843 = stream.ReadByte();
                aByte855 = stream.ReadByte();
            } else if(opcode == 158) {
                aByte833 = (byte) 1;
            } else if(opcode == 159) {
                aByte833 = (byte) 0;
            } else if(opcode == 160) {
                int i = stream.ReadByte();
                anIntArray8493 = new int[i];
                for(int i_58_ = 0; i > i_58_; i_58_++)
                    anIntArray8493[i_58_] = stream.ReadShort();
            } else if(opcode == 162)
                unknownBoolean7 = true;
            else if(opcode == 163)
                anInt864 = stream.ReadByte();
            else if(opcode == 164) {
                anInt848 = stream.ReadShort();
                anInt837 = stream.ReadShort();
            } else if(opcode == 165)
                anInt847 = stream.ReadByte();
            else if(opcode == 168)
                anInt828 = stream.ReadByte();
            else if(opcode >= 170 && opcode < 176) {
                stream.ReadShort();
            } else if(opcode == 179) {
                stream.ReadByte();
                stream.ReadByte();
                stream.ReadByte();
                stream.ReadByte();
                stream.ReadByte();
                stream.ReadByte();
            } else if(opcode == 249) {
                int i = stream.ReadByte();
                /*
                if(config == null)
                    config = new HashMap<Integer, Object>(i);
                    */
                for(int k = 0; k < i; k++) {
                    bool stringInstance = stream.ReadByte() == 1;
                    int key = stream.ReadMedium();
                    /*
                    Object value;
                    if(stringInstance)
                        value = stream.ReadString();
                    else
                        value = stream.ReadInt();
                    config.put(key, value);
                    */
                }
            }
        }

        /// <summary>
        /// Overrides a specific property corresponding to opcode
        /// </summary>
        /// <param name="stream">The stream to read from</param>
        /// <param name="opcode">The opcode value signalling which type to read</param>
        public JagStream encode() {
            JagStream stream = new JagStream();

            return stream;
        }

        internal void setId(int id) {
            this.id = id;
        }
    }
}