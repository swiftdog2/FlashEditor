using static FlashEditor.utils.DebugUtil;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor.Definitions
{
    /// <summary>
    /// Represents a single “loc” / world-object definition.
    /// </summary>
    public class ObjectDefinition : ICloneable, IDefinition
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

        /// <summary>
        /// Encodes this <see cref="ObjectDefinition"/> back into its binary form.
        /// </summary>
        /// <returns>Serialized definition stream.</returns>
        public JagStream Encode()
        {
            var o = new JagStream();

            void Emit(int op, Action payload = null)
            {
                o.WriteByte((byte)op);
                payload?.Invoke();
            }

            /* basic scalar fields */
            if (!string.IsNullOrEmpty(name))
                Emit(2, () => o.WriteJagexString(name));

            if (sizeY != 1)
                Emit(14, () => o.WriteByte(sizeY));

            if (!walkable)
            {
                if (decoded[15]) Emit(15);
                if (decoded[17]) Emit(17);
                if (decoded[18]) Emit(18);
            }

            if (isClipped)
                Emit(22);

            if (modelBrightness != 0)
                Emit(28, () => o.WriteByte((byte)(modelBrightness >> 2)));

            if (modelContrast != 0)
                Emit(29, () => o.WriteSignedByte((sbyte)modelContrast));

            /* action strings */
            for (int i = 0; i < actions.Length; i++)
                if (actions[i] != null)
                    Emit(30 + i, () => o.WriteJagexString(actions[i]));

            /* recolour */
            if (recolSrc != null)
                Emit(40, () =>
                {
                    o.WriteByte((byte)recolSrc.Length);
                    for (int i = 0; i < recolSrc.Length; i++)
                    {
                        o.WriteShort(recolSrc[i]);
                        o.WriteShort(recolDst[i]);
                    }
                });

            /* retexture */
            if (retexSrc != null)
                Emit(41, () =>
                {
                    o.WriteByte((byte)retexSrc.Length);
                    for (int i = 0; i < retexSrc.Length; i++)
                    {
                        o.WriteShort(retexSrc[i]);
                        o.WriteShort(retexDst[i]);
                    }
                });

            /* morph */
            if (morphIds != null)
            {
                bool use92 = decoded[92];
                int op = use92 ? 92 : 77;
                Emit(op, () =>
                {
                    o.WriteShort(morphVarbit == -1 ? 0xFFFF : morphVarbit);
                    o.WriteShort(morphVarp == -1 ? 0xFFFF : morphVarp);
                    int lastIndex = morphIds.Length - 1;
                    if (use92)
                        o.WriteShort(morphIds[lastIndex] == -1 ? 0xFFFF : morphIds[lastIndex]);

                    int count = morphIds.Length - 2;
                    o.WriteByte((byte)count);
                    for (int i = 0; i <= count; i++)
                        o.WriteShort(morphIds[i] == -1 ? 0xFFFF : morphIds[i]);
                });
            }

            /* sounds */
            if (ambientSoundId != -1)
            {
                if (extraSounds == null)
                    Emit(78, () =>
                    {
                        o.WriteShort(ambientSoundId);
                        o.WriteByte((byte)ambientSoundLoops);
                    });
                else
                    Emit(79, () =>
                    {
                        o.WriteShort(ambientSoundId);
                        o.WriteByte((byte)extraSounds.Length);
                        foreach (var s in extraSounds)
                            o.WriteShort(s);
                    });
            }

            /* menu options */
            for (int i = 0; i < menuOps.Length; i++)
                if (menuOps[i] != null)
                    Emit(150 + i, () => o.WriteJagexString(menuOps[i]));

            /* params */
            if (parameters != null && parameters.Count > 0)
                Emit(249, () =>
                {
                    o.WriteByte((byte)parameters.Count);
                    foreach (var kv in parameters)
                    {
                        bool isStr = kv.Value is string;
                        o.WriteByte((byte)(isStr ? 1 : 0));
                        o.WriteMedium(kv.Key);
                        if (isStr) o.WriteJagexString((string)kv.Value);
                        else o.WriteInteger((int)kv.Value);
                    }
                });

            /* terminator */
            o.WriteByte(0);
            return o.Flip();
        }
    }
}
