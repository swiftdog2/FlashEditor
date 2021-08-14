using System;
using static FlashEditor.utils.DebugUtil;

namespace FlashEditor.Definitions.Tracks {
    class Track {
        /**
         * The class that decodes RuneScape's custom packed music format into raw midi format.
         * 
         * References
         * 
         * https://www.rune-server.ee/runescape-development/rs-503-client-server/snippets/311669-rs-music-file-structure-conversion.html
         * https://github.com/runelite/runelite/blob/master/cache/src/main/java/net/runelite/cache/definitions/loaders/TrackLoader.java
         * 
         * Modified by Freyr
         */

        private byte[] decoded;

        public void Decode(JagStream buf) {
            buf.Position = buf.Seek(buf.Length - 3);
            int tracks = buf.ReadUnsignedByte();
            int division = buf.ReadUnsignedShort();
            int offset = 14 + tracks * 10;
            buf.Seek0();
            int tempoOpcodes = 0;
            int ctrlChangeOpcodes = 0;
            int noteOnOpcodes = 0;
            int noteOffOpcodes = 0;
            int wheelChangeOpcodes = 0;
            int chnnlAfterTchOpcodes = 0;
            int keyAfterTchOpcodes = 0;
            int progmChangeOpcodes = 0;

            int var13;
            int opcode;
            int var15;
            for(var13 = 0; var13 < tracks; ++var13) {
                opcode = -1;

                while(true) {
                    var15 = buf.ReadUnsignedByte();
                    if(var15 != opcode) {
                        ++offset;
                    }

                    opcode = var15 & 15;
                    if(var15 == 7) {
                        break;
                    }

                    if(var15 == 23) {
                        ++tempoOpcodes;
                    } else if(opcode == 0) {
                        ++noteOnOpcodes;
                    } else if(opcode == 1) {
                        ++noteOffOpcodes;
                    } else if(opcode == 2) {
                        ++ctrlChangeOpcodes;
                    } else if(opcode == 3) {
                        ++wheelChangeOpcodes;
                    } else if(opcode == 4) {
                        ++chnnlAfterTchOpcodes;
                    } else if(opcode == 5) {
                        ++keyAfterTchOpcodes;
                    } else {
                        if(opcode != 6) {
                            Debug("wtf?");
                        }

                        ++progmChangeOpcodes;
                    }
                }
            }

            offset += 5 * tempoOpcodes;
            offset += 2 * (noteOnOpcodes + noteOffOpcodes + ctrlChangeOpcodes + wheelChangeOpcodes + keyAfterTchOpcodes);
            offset += chnnlAfterTchOpcodes + progmChangeOpcodes;
            var13 = (int) buf.Position;
            opcode = tracks + tempoOpcodes + ctrlChangeOpcodes + noteOnOpcodes + noteOffOpcodes + wheelChangeOpcodes
                    + chnnlAfterTchOpcodes + keyAfterTchOpcodes + progmChangeOpcodes;

            for(var15 = 0; var15 < opcode; ++var15)
                buf.ReadVarInt();

            offset += (int) buf.Position - var13;
            var15 = (int) buf.Position;
            int var16 = 0;
            int var17 = 0;
            int var18 = 0;
            int var19 = 0;
            int var20 = 0;
            int var21 = 0;
            int var22 = 0;
            int var23 = 0;
            int var24 = 0;
            int var25 = 0;
            int var26 = 0;
            int var27 = 0;
            int var28 = 0;

            int var29;
            for(var29 = 0; var29 < ctrlChangeOpcodes; ++var29) {
                var28 = var28 + (buf.ReadUnsignedByte()) & 127;
                if(var28 != 0 && var28 != 32) {
                    if(var28 == 1) {
                        ++var16;
                    } else if(var28 == 33) {
                        ++var17;
                    } else if(var28 == 7) {
                        ++var18;
                    } else if(var28 == 39) {
                        ++var19;
                    } else if(var28 == 10) {
                        ++var20;
                    } else if(var28 == 42) {
                        ++var21;
                    } else if(var28 == 99) {
                        ++var22;
                    } else if(var28 == 98) {
                        ++var23;
                    } else if(var28 == 101) {
                        ++var24;
                    } else if(var28 == 100) {
                        ++var25;
                    } else if(var28 != 64 && var28 != 65 && var28 != 120 && var28 != 121 && var28 != 123) {
                        ++var27;
                    } else {
                        ++var26;
                    }
                } else {
                    ++progmChangeOpcodes;
                }
            }

            var29 = 0;

            int var30 = (int) buf.Position;
            buf.Skip(var26);

            int var31 = (int) buf.Position;
            buf.Skip(keyAfterTchOpcodes);

            int var32 = (int) buf.Position;
            buf.Skip(chnnlAfterTchOpcodes);

            int var33 = (int) buf.Position;
            buf.Skip(wheelChangeOpcodes);

            int var34 = (int) buf.Position;
            buf.Skip(var16);

            int var35 = (int) buf.Position;
            buf.Skip(var18);

            int var36 = (int) buf.Position;
            buf.Skip(var20);

            int var37 = (int) buf.Position;
            buf.Skip(noteOnOpcodes + noteOffOpcodes + keyAfterTchOpcodes);

            int var38 = (int) buf.Position;
            buf.Skip(noteOnOpcodes);

            int var39 = (int) buf.Position;
            buf.Skip(var27);

            int var40 = (int) buf.Position;
            buf.Skip(noteOffOpcodes);

            int var41 = (int) buf.Position;
            buf.Skip(var17);

            int var42 = (int) buf.Position;
            buf.Skip(var19);

            int var43 = (int) buf.Position;
            buf.Skip(var21);

            int var44 = (int) buf.Position;
            buf.Skip(progmChangeOpcodes);

            int var45 = (int) buf.Position;
            buf.Skip(wheelChangeOpcodes);

            int var46 = (int) buf.Position;
            buf.Skip(var22);

            int var47 = (int) buf.Position;
            buf.Skip(var23);

            int var48 = (int) buf.Position;
            buf.Skip(var24);

            int var49 = (int) buf.Position;
            buf.Skip(var25);

            int var50 = (int) buf.Position;
            buf.Skip(tempoOpcodes * 3);

            JagStream midiBuff = new JagStream(offset + 1);

            midiBuff.WriteInteger(1297377380); // MThd header
            midiBuff.WriteInteger(6); // length of header
            midiBuff.WriteShort((short) (tracks > 1 ? 1 : 0)); // format
            midiBuff.WriteShort((short) tracks); // tracks
            midiBuff.WriteShort((short) division); // division

            buf.Seek(var13);

            int var52 = 0;
            int var53 = 0;
            int var54 = 0;
            int var55 = 0;
            int var56 = 0;
            int var57 = 0;
            int var58 = 0;
            int[] var59 = new int[128];
            var28 = 0;

label361:
            { }
            for(int var60 = 0; var60 < tracks; ++var60) {
                midiBuff.WriteInteger(1297379947); // MTrk
                midiBuff.Skip(4); // length gets written here later
                int var61 = (int) midiBuff.Position;
                int var62 = -1;

                while(true) {
                    int var63 = buf.ReadVarInt();

                    midiBuff.WriteVarInt(var63); // delta time

                    int var64 = buf.ToArray()[var29++] & 255;
                    bool var65 = var64 != var62;
                    var62 = var64 & 15;
                    if(var64 == 7) {
                        // if (var65) -- client has this if, but it causes broken
                        // midi to be produced
                        {
                            midiBuff.WriteByte((byte) 255);
                        }

                        midiBuff.WriteByte((byte) 47); // type - end of track
                        midiBuff.WriteByte((byte) 0); // length
                        midiBuff.PutLengthFromMark(midiBuff.Position - var61);
                        goto label361;
                    }

                    if(var64 == 23) {
                        // if (var65) -- client has this if, but it causes broken
                        // midi to be produced
                        {
                            midiBuff.WriteByte((byte) 255); // meta event FF
                        }

                        midiBuff.WriteByte((byte) 81); // type - set tempo
                        midiBuff.WriteByte((byte) 3); // length
                        midiBuff.WriteByte((byte) buf.ToArray()[var50++]);
                        midiBuff.WriteByte((byte) buf.ToArray()[var50++]);
                        midiBuff.WriteByte((byte) buf.ToArray()[var50++]);
                    } else {
                        var52 ^= var64 >> 4;
                        if(var62 == 0) {
                            if(var65) {
                                midiBuff.WriteByte((byte) (144 + var52));
                            }

                            var53 += buf.ToArray()[var37++];
                            var54 += buf.ToArray()[var38++];
                            midiBuff.WriteByte((byte) (var53 & 127));
                            midiBuff.WriteByte((byte) (var54 & 127));
                        } else if(var62 == 1) {
                            if(var65) {
                                midiBuff.WriteByte((byte) (128 + var52));
                            }

                            var53 += buf.ToArray()[var37++];
                            var55 += buf.ToArray()[var40++];
                            midiBuff.WriteByte((byte) (var53 & 127));
                            midiBuff.WriteByte((byte) (var55 & 127));
                        } else if(var62 == 2) {
                            if(var65) {
                                midiBuff.WriteByte((byte) (176 + var52));
                            }

                            var28 = var28 + buf.ToArray()[var15++] & 127;
                            midiBuff.WriteByte((byte) var28);
                            byte var66;
                            if(var28 != 0 && var28 != 32) {
                                if(var28 == 1) {
                                    var66 = buf.ToArray()[var34++];
                                } else if(var28 == 33) {
                                    var66 = buf.ToArray()[var41++];
                                } else if(var28 == 7) {
                                    var66 = buf.ToArray()[var35++];
                                } else if(var28 == 39) {
                                    var66 = buf.ToArray()[var42++];
                                } else if(var28 == 10) {
                                    var66 = buf.ToArray()[var36++];
                                } else if(var28 == 42) {
                                    var66 = buf.ToArray()[var43++];
                                } else if(var28 == 99) {
                                    var66 = buf.ToArray()[var46++];
                                } else if(var28 == 98) {
                                    var66 = buf.ToArray()[var47++];
                                } else if(var28 == 101) {
                                    var66 = buf.ToArray()[var48++];
                                } else if(var28 == 100) {
                                    var66 = buf.ToArray()[var49++];
                                } else if(var28 != 64 && var28 != 65 && var28 != 120 && var28 != 121 && var28 != 123) {
                                    var66 = buf.ToArray()[var39++];
                                } else {
                                    var66 = buf.ToArray()[var30++];
                                }
                            } else {
                                var66 = buf.ToArray()[var44++];
                            }

                            int var67 = var66 + var59[var28];
                            var59[var28] = var67;
                            midiBuff.WriteByte((byte) (var67 & 127));
                        } else if(var62 == 3) {
                            if(var65) {
                                midiBuff.WriteByte((byte) (224 + var52));
                            }

                            var56 += buf.ToArray()[var45++];
                            var56 += buf.ToArray()[var33++] << 7;
                            midiBuff.WriteByte((byte) (var56 & 127));
                            midiBuff.WriteByte((byte) (var56 >> 7 & 127));
                        } else if(var62 == 4) {
                            if(var65) {
                                midiBuff.WriteByte((byte) (208 + var52));
                            }

                            var57 += buf.ToArray()[var32++];
                            midiBuff.WriteByte((byte) (var57 & 127));
                        } else if(var62 == 5) {
                            if(var65) {
                                midiBuff.WriteByte((byte) (160 + var52));
                            }

                            var53 += buf.ToArray()[var37++];
                            var58 += buf.ToArray()[var31++];
                            midiBuff.WriteByte((byte) (var53 & 127));
                            midiBuff.WriteByte((byte) (var58 & 127));
                        } else {
                            if(var62 != 6)
                                throw new Exception();

                            if(var65)
                                midiBuff.WriteByte((byte) (192 + var52));

                            midiBuff.WriteByte((byte) buf.ToArray()[var44++]);
                        }
                    }
                }
            }

            midiBuff.Flip();

            decoded = midiBuff.ToArray();
        }

        public int GetID() {
            return 0;
        }

        public byte[] GetDecoded() {
            return decoded;
        }
    }
}
