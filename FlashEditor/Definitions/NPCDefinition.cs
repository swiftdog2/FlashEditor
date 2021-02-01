using FlashEditor.utils;
using System;

namespace FlashEditor {
    internal class NPCDefinition {
        public string[] options;
        public string name;

        public bool clickable, isVisible, drawMinimapDot, visiblePriority, slowWalk, invisiblePriority, animateIdle;

        public bool unknownBoolean7;
        public int[] cursorOps;
        private int movementType;
        private byte[] aByteArray8445, aByteArray8446;

        public int[][] translations, anIntArrayArray882;
        public int[] morphs, recolorDst, retextureDst, modelIds, retextureSrc, campaigns, dialogueModels, recolorSrc;

        public int id, primaryCursorOp, hitbarSprite, secondaryCursorOp, size = 1, crawlSound, varbit, height, renderTypeID, ambientSoundVolume,
            rotation, level, scaleXY, ambient, mapIcon, runSound, attackOpCursor, primaryCursor, soundDistance, idleSound,
            headIcon, spriteId, walkSound, varp, contrast, scaleZ, armyIcon, secondaryCursor;

        int hue, saturation, lightness, opacity, anInt864, anInt848, anInt837, anInt847, anInt828;

        public short primaryShadowColour, secondaryShadowColour;

        public byte[] recolorDstPalette;
        public byte mainOptionIndex, primaryShadowModifier, secondaryShadowModifier, walkMask, respawnDirection;


        /// <summary>
        /// Constructs a new item definition from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the encoded item data</param>
        public NPCDefinition(JagStream stream) {
            setDefaults();
            Decode(stream);
        }

        public void setDefaults() {
            options = new[] { null, null, null, null, null, "Examine" };
            name = "null";
            level = -1;
            drawMinimapDot = true;
            renderTypeID = -1;
            respawnDirection = 7;
            size = 1;
            crawlSound = -1;
            varbit = -1;
            secondaryCursorOp = -1;
            height = -1;
            rotation = 32;
            hitbarSprite = -1;
            ambient = 0;
            walkMask = 0;
            ambientSoundVolume = 255;
            runSound = -1;
            slowWalk = true;
            primaryShadowColour = 0;
            idleSound = -1;
            primaryShadowModifier = 223;
            soundDistance = 0;
            attackOpCursor = -1;
            animateIdle = true;
            mapIcon = -1;
            primaryCursor = -1;
            primaryCursorOp = -1;
            scaleXY = 128;
            headIcon = -1;
            invisiblePriority = false;
            varp = -1;
            secondaryShadowModifier = 143;
            isVisible = false;
            secondaryCursor = -1;
            walkSound = -1;
            clickable = true;
            spriteId = -1;
            scaleZ = 128;
            secondaryShadowColour = 0;
            contrast = 0;
            armyIcon = -1;
        }

        /// <summary>
        /// Overrides defaults from the stream
        /// </summary>
        /// <param name="stream"></param>
        public void Decode(JagStream stream) {
            if(stream != null) {
                while(true) {
                    int opcode = stream.ReadUnsignedByte();

                    if(opcode == 0 || opcode == 255)
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
            //return;

            if(opcode == 1) {
                int length = stream.ReadByte();
                modelIds = new int[length];
                for(int k = 0; k < length; k++) {
                    modelIds[k] = stream.ReadShort();
                    if((modelIds[k] ^ 0xffffffff) == -65536)
                        modelIds[k] = -1;
                }
            } else if(opcode == 2) {
                name = stream.ReadString();
            } else if(opcode == 12) {
                size = stream.ReadByte();
            } else if(opcode >= 30 && opcode < 34) { //was 36 before
                options[opcode - 30] = stream.ReadString();
                if(options[opcode - 30] == "Hidden")
                    options[opcode - 30] = null;
            } else if(opcode == 40) {
                //Read colours
                int length = stream.ReadByte();
                recolorDst = new int[length];
                recolorSrc = new int[length];
                for(int count = 0; (length ^ 0xffffffff) < (count ^ 0xffffffff); count++) {
                    recolorSrc[count] = stream.ReadShort();
                    recolorDst[count] = stream.ReadShort();
                }
            } else if(opcode == 41) {
                //Read textures
                int i = stream.ReadByte();
                retextureSrc = new int[i];
                retextureDst = new int[i];
                for(int i_54_ = 0; (i_54_ ^ 0xffffffff) > (i ^ 0xffffffff); i_54_++) {
                    retextureSrc[i_54_] = stream.ReadShort();
                    retextureDst[i_54_] = stream.ReadShort();
                }
            } else if(opcode == 42) {
                int length = stream.ReadByte();
                recolorDstPalette = new byte[length];
                for(int i_55_ = 0; length > i_55_; i_55_++) {
                    recolorDstPalette[i_55_] = (byte) stream.ReadByte();
                }
            } else if(opcode == 44) {
                stream.ReadShort();
            } else if(opcode == 45) {
                stream.ReadShort();
            } else if(opcode == 60) {
                int length = stream.ReadByte();
                dialogueModels = new int[length];
                for(int count = 0; (count ^ 0xffffffff) > (length ^ 0xffffffff); count++)
                    dialogueModels[count] = stream.ReadShort();
            } else if(opcode == 93) {
                drawMinimapDot = false;
            } else if(opcode == 95) {
                level = stream.ReadShort();
            } else if(opcode == 97) {
                scaleXY = stream.ReadShort();
            } else if(opcode == 98) {
                scaleZ = stream.ReadShort();
            } else if(opcode == 99) {
                isVisible = true;
            } else if(opcode == 100) {
                ambient = stream.ReadByte();
            } else if(opcode == 101) {
                contrast = stream.ReadByte();
            } else if(opcode == 102) {
                headIcon = stream.ReadShort();
            } else if(opcode == 103) {
                rotation = stream.ReadShort();
            } else if(opcode == 106 || opcode == 118) {
                varbit = stream.ReadShort();
                if(varbit == 65535)
                    varbit = -1;

                varp = stream.ReadShort();
                if(varp == 65535)
                    varp = -1;

                int last = -1;
                if(opcode == 118) {
                    last = stream.ReadShort();
                    if(last == 65535)
                        last = -1;
                }

                int count = stream.ReadByte();
                morphs = new int[2 + count];
                for(int index = 0; count >= index; index++) {
                    morphs[index] = stream.ReadShort();
                    if(morphs[index] == 65535)
                        morphs[index] = -1;
                }
                morphs[count + 1] = last;
            } else if(opcode == 107)
                clickable = false;
            else if(opcode == 109)
                slowWalk = false;
            else if(opcode == 111)
                animateIdle = false;
            else if(opcode == 113) {
                primaryShadowColour = (short) (stream.ReadShort());
                secondaryShadowColour = (short) (stream.ReadShort());
            } else if(opcode == 114) {
                primaryShadowModifier = (byte) (stream.ReadByte());
                secondaryShadowModifier = (byte) (stream.ReadByte());
            } else if(opcode == 119)
                walkMask = (byte) (stream.ReadByte());
            else if(opcode == 121) {
                //Translations
                
                translations = new int[modelIds == null ? 0 : modelIds.Length][];
                int length = (stream.ReadByte());
                for(int i_62_ = 0; ((i_62_ ^ 0xffffffff) > (length ^ 0xffffffff)); i_62_++) {
                    int index = stream.ReadByte();
                    int[] nigga = (translations[index] = (new int[3]));
                    nigga[0] = stream.ReadByte();
                    nigga[1] = stream.ReadByte();
                    nigga[2] = stream.ReadByte();
                }
            } else if(opcode == 122)
                hitbarSprite = (stream.ReadShort());
            else if(opcode == 123)
                height = (stream.ReadShort());
            else if(opcode == 125)
                respawnDirection = (byte) (stream.ReadByte());
            else if(opcode == 127)
                renderTypeID = (stream.ReadShort());
            else if(opcode == 128)
                movementType = stream.ReadByte();
            else if(opcode == 134) {
                idleSound = stream.ReadShort();
                if(idleSound == 65535)
                    idleSound = -1;

                crawlSound = (stream.ReadShort());
                if(crawlSound == 65535)
                    crawlSound = -1;

                walkSound = stream.ReadShort();
                if((walkSound ^ 0xffffffff) == -65536)
                    walkSound = -1;

                runSound = (stream.ReadShort());
                if((runSound ^ 0xffffffff) == -65536)
                    runSound = -1;

                soundDistance = stream.ReadByte();
            } else if(opcode == 135) {
                primaryCursorOp = stream.ReadByte();
                primaryCursor = stream.ReadShort();
            } else if(opcode == 136) {
                secondaryCursorOp = stream.ReadByte();
                secondaryCursor = stream.ReadShort();
            } else if(opcode == 137)
                attackOpCursor = stream.ReadShort();
            else if(opcode == 138)
                armyIcon = stream.ReadShort();
            else if(opcode == 139)
                spriteId = stream.ReadShort();
            else if(opcode == 140)
                ambientSoundVolume = stream.ReadByte();
            else if(opcode == 141)
                visiblePriority = true;
            else if(opcode == 142)
                mapIcon = stream.ReadShort();
            else if(opcode == 143) {
                invisiblePriority = true;
            } else if(opcode >= 150 && opcode < 155) {
                options[opcode - 150] = stream.ReadString();
                if(options[opcode - 150] == "Hidden")
                    options[opcode - 150] = null;
            } else if(opcode == 155) {
                hue = stream.ReadByte();
                saturation = stream.ReadByte();
                lightness = stream.ReadByte();
                opacity = stream.ReadByte();
            } else if(opcode == 158) {
                mainOptionIndex = (byte) 1;
            } else if(opcode == 159) {
                mainOptionIndex = (byte) 0;
            } else if(opcode == 160) {
                int length = stream.ReadByte();
                campaigns = new int[length];
                for(int count = 0; length > count; count++)
                    campaigns[count] = stream.ReadShort();
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