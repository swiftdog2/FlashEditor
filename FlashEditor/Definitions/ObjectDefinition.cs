using static FlashEditor.utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor.Definitions
{
    /// <summary>
    /// Represents a single “loc” / world-object definition.
    /// </summary>
    internal sealed class ObjectDefinition : ICloneable, IDefinition
    {
        /*───────────────────────────────────────────*
         *  ▌  Static/shared helpers                ▐
         *───────────────────────────────────────────*/
        private static readonly StringBuilder SharedBuilder = new StringBuilder();

        /*───────────────────────────────────────────*
         *  ▌  Public fields (RS cache values)      ▐
         *     — Only those touched by opcodes are  *
         *       declared here; add more as you map *
         *───────────────────────────────────────────*/
        public string name;
        public int id;

        public bool[] decoded = new bool[256];          // opcode hit-map
        public string[] actions = new string[5];          // opcodes 30-34
        public string[] menuOps = new string[5];          // opcodes 150-154

        // Geometry / render flags
        public byte sizeX = 1, sizeY = 1;
        public bool walkable = true;
        public bool isClipped = false;
        public int modelBrightness, modelContrast;

        // Morph varbits / varps (op 77 / 92)
        public int morphVarbit = -1;
        public int morphVarp = -1;
        public int[] morphIds;

        // Sound / ambience
        public int ambientSoundId = -1;
        public int ambientSoundLoops;
        public int[] extraSounds;

        // Colour / texture swaps
        public short[] recolSrc, recolDst;
        public short[] retexSrc, retexDst;

        // Params (opcode 249)
        public SortedDictionary<int, object> parameters;

        /*───────────────────────────────────────────*
         *  ▌  Clone support                        ▐
         *───────────────────────────────────────────*/
        public ObjectDefinition Clone() => (ObjectDefinition)MemberwiseClone();
        object ICloneable.Clone() => Clone();

        /*───────────────────────────────────────────*
         *  ▌  Public decode entry-point            ▐
         *───────────────────────────────────────────*/
        /// <summary>
        /// Fully decodes an <see cref="ObjectDefinition"/> from the supplied <see cref="JagStream"/>.
        /// </summary>
        /// <param name="stream">Stream positioned at the start of the definition blob.</param>
        /// <returns>Populated <see cref="ObjectDefinition"/> instance.</returns>
        public void Decode(JagStream stream)
        {
            int count = 0;

            SharedBuilder.Clear();

            while (true)
            {
                int opcode = stream.ReadUnsignedByte();
                decoded[opcode] = true;

                if (opcode == 0) break;          // terminator
                Decode(stream, opcode);

                if (++count > 256) break;        // safety guard
            }

            for (int i = 0; i < decoded.Length; i++)
                if (decoded[i]) SharedBuilder.Append(i).Append(' ');

            Debug($"ObjectDef {id} ({name ?? "null"}) OPCODES: {SharedBuilder}", LOG_DETAIL.NONE);
        }

        public static ObjectDefinition DecodeFromStream(JagStream stream)
        {
            var def = new ObjectDefinition();
            def.Decode(stream);
            return def;
        }

        /*───────────────────────────────────────────*
         *  ▌  Private per-opcode handler           ▐
         *───────────────────────────────────────────*/
        /// <summary>
        /// Handles a single opcode inside the definition stream.
        /// </summary>
        /// <param name="buf">Data stream.</param>
        /// <param name="op">Opcode byte already read.</param>
        private void Decode(JagStream buf, int op)
        {
            switch (op)
            {
                /*──────── 1 / 5 : model lists ────────*/
                case 1:
                case 5:
                    {
                        int groupCount = buf.ReadUnsignedByte();
                        /* You can store the raw model data here if needed.
                           Skipping for brevity – just seek past it.           */
                        for (int g = 0; g < groupCount; g++)
                        {
                            buf.ReadSignedByte();                      // model type
                            int modelCt = buf.ReadUnsignedByte();
                            for (int m = 0; m < modelCt; m++) buf.ReadUnsignedShort();
                        }
                        break;
                    }

                /*──────── scalar flags ────────*/
                case 2: name = buf.ReadJagexString(); break;
                case 14: sizeY = buf.ReadUnsignedByte(); break;
                case 15: walkable = false; break;
                case 17: walkable = false; /*actionCount=0*/      break;
                case 18: walkable = false; break;
                case 19: /* some int */  buf.ReadUnsignedByte(); break;
                case 22: /* non-solid */ isClipped = true; break;
                case 24: /* animation id */ buf.ReadUnsignedShort(); break;
                case 28: modelBrightness = buf.ReadUnsignedByte() << 2; break;
                case 29: modelContrast = buf.ReadSignedByte(); break;

                /*──────── action strings 30-34 ────────*/
                case int a when a >= 30 && a < 35:
                    actions[a - 30] = buf.ReadJagexString();
                    break;

                /*──────── recolour 40 ────────*/
                case 40:
                    {
                        int n = buf.ReadUnsignedByte();
                        recolSrc = new short[n];
                        recolDst = new short[n];
                        for (int i = 0; i < n; i++)
                        {
                            recolSrc[i] = (short)buf.ReadUnsignedShort();
                            recolDst[i] = (short)buf.ReadUnsignedShort();
                        }
                        break;
                    }

                /*──────── retexture 41 ────────*/
                case 41:
                    {
                        int n = buf.ReadUnsignedByte();
                        retexSrc = new short[n];
                        retexDst = new short[n];
                        for (int i = 0; i < n; i++)
                        {
                            retexSrc[i] = (short)buf.ReadUnsignedShort();
                            retexDst[i] = (short)buf.ReadUnsignedShort();
                        }
                        break;
                    }

                /*──────── light/shadow 65-67 ────────*/
                case 65: /* ambience */ buf.ReadUnsignedShort(); break;
                case 66: /* contrast */ buf.ReadUnsignedShort(); break;
                case 67: /* scaleZ  */ buf.ReadShort(); break;

                /*──────── morph (77 / 92) ────────*/
                case 77:
                case 92:
                    {
                        morphVarbit = SmartOrMinus1(buf);
                        morphVarp = SmartOrMinus1(buf);
                        int defaultId = (op == 92) ? SmartOrMinus1(buf) : -1;

                        int ct = buf.ReadUnsignedByte();
                        morphIds = new int[ct + 2];
                        for (int i = 0; i <= ct; i++) morphIds[i] = SmartOrMinus1(buf);
                        morphIds[ct + 1] = defaultId;
                        break;
                    }

                /*──────── sounds 78 / 79 ────────*/
                case 78:
                    {
                        ambientSoundId = buf.ReadUnsignedShort();
                        ambientSoundLoops = buf.ReadUnsignedByte();
                        break;
                    }
                case 79:
                    {
                        ambientSoundId = buf.ReadUnsignedShort();
                        /* extra sound fields … */
                        int count = buf.ReadUnsignedByte();
                        extraSounds = new int[count];
                        for (int i = 0; i < count; i++) extraSounds[i] = buf.ReadUnsignedShort();
                        break;
                    }

                /*──────── menuOps 150-154 ────────*/
                case int m when m >= 150 && m < 155:
                    menuOps[m - 150] = buf.ReadJagexString();
                    break;

                /*──────── multi-map icons 160 ────────*/
                case 160:
                    {
                        int len = buf.ReadUnsignedByte();
                        /* ignore for now */
                        for (int i = 0; i < len; i++) buf.ReadUnsignedShort();
                        break;
                    }

                /*──────── arbitrary params 249 ────────*/
                case 249:
                    {
                        int len = buf.ReadUnsignedByte();
                        parameters = new SortedDictionary<int, object>();

                        for (int i = 0; i < len; i++)
                        {
                            bool isString = buf.ReadUnsignedByte() == 1;
                            int key = buf.ReadMedium();

                            object value = isString ? (object)buf.ReadJagexString() : buf.ReadInt();
                            if (!parameters.ContainsKey(key)) parameters.Add(key, value);
                        }
                        break;
                    }

                /*──────── unhandled opcodes ────────*/
                default:
                    // Safely skip unknown opcode for now
                    break;
            }
        }

        /*───────────────────────────────────────────*
         *  ▌  Helpers                               ▐
         *───────────────────────────────────────────*/
        private static int SmartOrMinus1(JagStream buf)
        {
            int val = buf.ReadUnsignedShort();
            return val == 0xFFFF ? -1 : val;
        }

        public JagStream Encode()
        {
            throw new NotImplementedException();
        }
    }
}
