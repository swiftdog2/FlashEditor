
/* Class98_Sub22 - Decompiled by JODE
 * Visit http://jode.sourceforge.net/
 */

import java.math.BigInteger;

class RSBuffer extends Node {
    
    static IncomingOpcode aClass58_3993;
    static int anInt3994 = -1;
    static JS5Archive aJS5Archive_3995;
    
    static {
        aClass58_3993 = new IncomingOpcode(114, 3);
    }
    
    static final void method1216(int i) {
        
        try {
            if(i != -17470) {
                method1216(-14);
            }
            InterfaceSettings interfaceSettings = Class185.method2628(0, -42, 15);
            interfaceSettings.method1621(0);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.HC(" + i + ')');
        }
    }
    
    static final boolean method1241(boolean bool, int i, int i_78_) {
        
        try {
            if(bool != false) {
                return false;
            }
            return (0x100 & i_78_) != 0;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("ji.L(" + bool + ',' + i + ',' + i_78_ + ')'));
        }
    }
    
    public static void method1243(int i) {
        
        try {
            aJS5Archive_3995 = null;
            if(i <= 79) {
                anInt3994 = -43;
            }
            // aClass58_3993 = null;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.JC(" + i + ')');
        }
    }
    
    int caret;
    
    byte[] buffer;
    
    RSBuffer(byte[] is) {
        
        try {
            this.caret = 0;
            this.buffer = is;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.<init>(" + (is != null ? "{...}" : "null") + ')');
        }
    }
    
    RSBuffer(int i) {
        
        try {
            this.buffer = Class129.method2225(false, i);
            this.caret = 0;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.<init>(" + i + ')');
        }
    }
    
    final void addBEInt(byte i, int i_16_) {
        
        try {
            this.buffer[this.caret++] = (byte) (i_16_ >> 8);
            this.buffer[this.caret++] = (byte) i_16_;
            this.buffer[this.caret++] = (byte) (i_16_ >> 24);
            this.buffer[this.caret++] = (byte) (i_16_ >> 16);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.MA(" + i + ',' + i_16_ + ')');
        }
    }
    
    final void method1181(String string, int i) {
        
        try {
            int i_0_ = string.indexOf('\0');
            if((i_0_ ^ 0xffffffff) <= i) {
                throw new IllegalArgumentException("NUL character at " + i_0_ + " - cannot pjstr2");
            }
            this.buffer[this.caret++] = (byte) 0;
            this.caret += Class200.method2694(string, 0, string.length(), this.caret, this.buffer, -28439);
            this.buffer[this.caret++] = (byte) 0;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("ji.GB(" + (string != null ? "{...}" : "null") + ',' + i + ')'));
        }
    }
    
    final int method1183(int i) {
        
        try {
            this.caret += 2;
            int i_2_ = (((this.buffer[-2 + this.caret]) & 0xff) + ((this.buffer[this.caret - 1]) << 8 & 0xff00));
            if((i_2_ ^ 0xffffffff) < -32768) {
                i_2_ -= 65536;
            }
            return i_2_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.KC(" + i + ')');
        }
    }
    
    final byte method1184(int i) {
        
        try {
            return (byte) ((this.buffer[this.caret++]) - 128);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.GA(" + i + ')');
        }
    }
    
    final int method1186() {
        this.caret += 3;
        return (((this.buffer[-1 + this.caret]) & 0xff) + ((0xff & (this.buffer[-2 + this.caret])) << 8)
            + (((this.buffer[this.caret + -3]) & 0xff) << 16));
    }
    
    final byte method1187(byte i) {
        
        try {
            return (byte) -(this.buffer[this.caret++]);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.WB(" + i + ')');
        }
    }
    
    final long method1189(byte i) {
        
        try {
            long l = method1202((byte) -58) & 0xffffffffL;
            long l_5_ = 0xffffffffL & method1202((byte) -68);
            return l + (l_5_ << 32);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.FC(" + i + ')');
        }
    }
    
    final void method1190(byte[] is, boolean bool, int i, int i_6_) {
        
        try {
            for(int i_7_ = i_6_; i_7_ < i_6_ + i; i_7_++) {
                is[i_7_] = (this.buffer[this.caret++]);
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("ji.CA(" + (is != null ? "{...}" : "null") + ',' + bool + ',' + i + ',' + i_6_ + ')'));
        }
    }
    
    final int method1192(byte i) {
        
        try {
            this.caret += 3;
            return ((0xff & (this.buffer[-3 + this.caret])) + (((0xff & (this.buffer[-2 + this.caret])) << 8)
                + (0xff0000 & ((this.buffer[this.caret - 1]) << 16))));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.BA(" + i + ')');
        }
    }
    
    final int method1198(int i) {
        
        try {
            this.caret += 2; // writeLEShortA
            int i_14_ = ((-128 + (this.buffer[this.caret - 2]) & 0xff)
                + ((this.buffer[this.caret + -1]) << 8 & 0xff00));
            if((i_14_ ^ 0xffffffff) < -32768) {
                i_14_ -= 65536;
            }
            return i_14_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.QA(" + i + ')');
        }
    }
    
    final void method1199(int i, boolean bool) {
        
        try {
            if(bool != false) {
                readInt1(true);
            }
            if((~0x7f & i) != 0) {
                if((~0x3fff & i) != 0) {
                    if((~0x1fffff & i ^ 0xffffffff) != -1) {
                        if((~0xfffffff & i) != 0) {
                            writeByte(i >>> 28 | 0x80);
                        }
                        writeByte((i | 0x1001c695) >>> 21);
                    }
                    writeByte((0x201d9a | i) >>> 14);
                }
                writeByte(0x80 | i >>> 7);
            }
            writeByte(i & 0x7f);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.QC(" + i + ',' + bool + ')');
        }
    }
    
    final void method1201(int i) {
        
        do {
            try {
                if(this.buffer != null) {
                    Class129.method2228((byte) 75, this.buffer);
                }
                this.buffer = null;
                if(i == 0) {
                    break;
                }
                method1216(-7);
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception, "ji.VA(" + i + ')');
            }
            break;
        } while(false);
    }
    
    final int method1202(byte i) {
        
        try {
            this.caret += 4;
            if(i >= -45) {
                anInt3994 = 37;
            }
            return (((this.buffer[-3 + this.caret]) << 8 & 0xff00)
                + (((this.buffer[this.caret + -2]) & 0xff) << 16)
                + (((0xff & (this.buffer[-1 + this.caret])) << 24)
                + (0xff & (this.buffer[-4 + this.caret]))));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.SA(" + i + ')');
        }
    }
    
    final void method1205(BigInteger biginteger, boolean bool, BigInteger biginteger_20_) {
        
        try {
            int i = this.caret;
            this.caret = 0;
            byte[] is = new byte[i];
            method1190(is, bool, i, 0);
            BigInteger biginteger_21_ = new BigInteger(is);
            BigInteger biginteger_22_ = biginteger_21_; // disabled
            byte[] is_23_ = biginteger_22_.toByteArray();
            this.caret = 0;
            if(bool == true) {
                writeShort(is_23_.length, 1571862888);
                method1217(is_23_, is_23_.length, -1, 0);
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("ji.IA(" + (biginteger != null ? "{...}" : "null") + ','
                + bool + ',' + (biginteger_20_ != null ? "{...}" : "null") + ')'));
        }
    }
    
    final void method1207(byte i, int i_25_) {
        
        try {
            this.buffer[this.caret - i_25_ + -2] = (byte) (i_25_ >> 8);
            if(i != 90) {
                readUnsignedByte();
            }
            this.buffer[-1 + (-i_25_ + this.caret)] = (byte) i_25_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.CC(" + i + ',' + i_25_ + ')');
        }
    }
    
    final int method1208(int i) {
        
        try {
            int i_26_ = 0;
            if(i != 3893) {
                return 116;
            }
            int i_27_;
            for(i_27_ = readSmart(i + 1689618819); i_27_ == 32767; i_27_ = readSmart(1689622712)) {
                i_26_ += 32767;
            }
            i_26_ += i_27_;
            return i_26_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.P(" + i + ')');
        }
    }
    
    final boolean method1210(int i) {
        
        try {
            this.caret -= 4;
            int i_29_ = Class365.method3937(this.buffer, this.caret, 0);
            int i_30_ = readInt();
            return i_29_ == i_30_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.EB(" + i + ')');
        }
    }
    
    final void method1211(byte i, int i_31_) {
        
        try {
            if(i > 79) {
                this.buffer[-i_31_ + (this.caret - 1)] = (byte) i_31_;
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.HB(" + i + ',' + i_31_ + ')');
        }
    }
    
    final void method1213(int i, long l, int i_32_) {
        
        try {
            if(--i_32_ < 0 || i_32_ > 7) {
                throw new IllegalArgumentException();
            }
            if(i != 31498) {
                method1208(4);
            }
            for(int i_33_ = 8 * i_32_; i_33_ >= 0; i_33_ -= 8) {
                this.buffer[this.caret++] = (byte) (int) (l >> i_33_);
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("ji.S(" + i + ',' + l + ',' + i_32_ + ')'));
        }
    }
    
    final void method1215(int[] is, int i, int i_34_, byte i_35_) {
        
        try {
            if(i_35_ != 30) {
                method1192((byte) -61);
            }
            int i_36_ = this.caret;
            this.caret = i;
            int i_37_ = (-i + i_34_) / 8;
            for(int i_38_ = 0; (i_38_ ^ 0xffffffff) > (i_37_ ^ 0xffffffff); i_38_++) {
                int i_39_ = readInt();
                int i_40_ = readInt();
                int i_41_ = -957401312;
                int i_42_ = -1640531527;
                int i_43_ = 32;
                while((i_43_-- ^ 0xffffffff) < -1) {
                    i_40_ -= (is[i_41_ >>> 11 & 0x5a600003] + i_41_ ^ ((i_39_ << 4 ^ i_39_ >>> 5) - -i_39_));
                    i_41_ -= i_42_;
                    i_39_ -= (i_41_ + is[i_41_ & 0x3] ^ i_40_ + (i_40_ << 4 ^ i_40_ >>> 5));
                }
                this.caret -= 8;
                writeInt(i_39_);
                writeInt(i_40_);
            }
            this.caret = i_36_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("ji.AB(" + (is != null ? "{...}" : "null") + ',' + i + ',' + i_34_ + ',' + i_35_ + ')'));
        }
    }
    
    final void method1217(byte[] is, int i, int i_44_, int i_45_) {
        
        try {
            int i_46_ = i_45_;
            if(i_44_ != -1) {
                anInt3994 = 121;
            }
            for(/**/; i + i_45_ > i_46_; i_46_++) {
                this.buffer[this.caret++] = is[i_46_];
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("ji.DB(" + (is != null ? "{...}" : "null") + ',' + i + ',' + i_44_ + ',' + i_45_ + ')'));
        }
    }
    
    final void method1218(int i, int i_47_) {
        
        try {
            this.buffer[this.caret++] = (byte) i;
            this.buffer[this.caret++] = (byte) (i >> 8);
            if(i_47_ != 1489446952) {
                this.buffer = null;
            }
            this.buffer[this.caret++] = (byte) (i >> 16);
            this.buffer[this.caret++] = (byte) (i >> 24);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.MC(" + i + ',' + i_47_ + ')');
        }
    }
    
    final void method1221(int i, long l) {
        
        try {
            this.buffer[this.caret++] = (byte) (int) (l >> 56);
            if(i > -49) {
                this.caret = -23;
            }
            this.buffer[this.caret++] = (byte) (int) (l >> 48);
            this.buffer[this.caret++] = (byte) (int) (l >> 40);
            this.buffer[this.caret++] = (byte) (int) (l >> 32);
            this.buffer[this.caret++] = (byte) (int) (l >> 24);
            this.buffer[this.caret++] = (byte) (int) (l >> 16);
            this.buffer[this.caret++] = (byte) (int) (l >> 8);
            this.buffer[this.caret++] = (byte) (int) l;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.JB(" + i + ',' + l + ')');
        }
    }
    
    final String method1222(int i) {
        
        try {
            if(((this.buffer[this.caret]) ^ 0xffffffff) == i) {
                this.caret++;
                return null;
            }
            return readString();
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.WA(" + i + ')');
        }
    }
    
    final String method1223(int i) {
        
        try {
            byte i_49_ = (this.buffer[this.caret++]);
            if((i_49_ ^ 0xffffffff) != -1) {
                throw new IllegalStateException("Bad version number in gjstr2");
            }
            int i_50_ = this.caret;
            while(((this.buffer[this.caret++]) ^ 0xffffffff) != -1) {
                /* empty */
            }
            if(i != -1) {
                return null;
            }
            int i_51_ = -1 + (this.caret + -i_50_);
            if(i_51_ == 0) {
                return "";
            }
            return Node_Sub46_Sub6.method1546(i_51_, i_50_, (byte) -108, (this.buffer));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.PB(" + i + ')');
        }
    }
    
    final void method1225(int i, int i_53_) {
        
        do {
            try {
                this.buffer[this.caret++] = (byte) (i_53_ >> 16);
                this.buffer[this.caret++] = (byte) (i_53_ >> 8);
                this.buffer[this.caret++] = (byte) i_53_;
                if(i == -24472) {
                    break;
                }
                // readInt(46);
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception, ("ji.RB(" + i + ',' + i_53_ + ')'));
            }
            break;
        } while(false);
    }
    
    final int method1227(byte i) {
        
        try {
            
            this.caret += 3;
            int i_55_ = ((((this.buffer[-2 + this.caret]) & 0xff) << 8)
                + (((this.buffer[this.caret - 3]) << 16 & 0xff0000)
                + ((this.buffer[-1 + this.caret]) & 0xff)));
            if((i_55_ ^ 0xffffffff) < -8388608) {
                i_55_ -= 16777216;
            }
            return i_55_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.LA(" + i + ')');
        }
    }
    
    final void method1231(int i, byte i_59_) {
        
        try {
            this.buffer[this.caret++] = (byte) (128 + i);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.Q(" + i + ',' + i_59_ + ')');
        }
    }
    
    final void method1232(int i, byte i_61_) {
        
        do {
            try {
                this.buffer[this.caret++] = (byte) (i >> 16);
                this.buffer[this.caret++] = (byte) (i >> 24);
                this.buffer[this.caret++] = (byte) i;
                this.buffer[this.caret++] = (byte) (i >> 8);
                if(i_61_ > 74) {
                    break;
                }
                anInt3994 = 115;
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception, ("ji.IB(" + i + ',' + i_61_ + ')'));
            }
            break;
        } while(false);
    }
    
    final void method1233(byte i, int i_62_) {
        
        try {
            this.buffer[-4 + (-i_62_ + this.caret)] = (byte) (i_62_ >> 24);
            if(i > -69) {
                method1190(null, false, -107, -119);
            }
            this.buffer[-3 + (-i_62_ + this.caret)] = (byte) (i_62_ >> 16);
            this.buffer[-i_62_ + this.caret + -2] = (byte) (i_62_ >> 8);
            this.buffer[-i_62_ + (this.caret + -1)] = (byte) i_62_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.O(" + i + ',' + i_62_ + ')');
        }
    }
    
    final byte method1234(int i) {
        
        try {
            return (byte) (-(this.buffer[this.caret++]) + 128);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.PC(" + i + ')');
        }
    }
    
    final void method1235(boolean bool, int[] is, int i, int i_63_) {
        
        do {
            try {
                int i_64_ = this.caret;
                this.caret = i;
                int i_65_ = (i_63_ - i) / 8;
                for(int i_66_ = 0; (i_66_ ^ 0xffffffff) > (i_65_ ^ 0xffffffff); i_66_++) {
                    int i_67_ = readInt();
                    int i_68_ = readInt();
                    int i_69_ = 0;
                    int i_70_ = -1640531527;
                    int i_71_ = 32;
                    while((i_71_-- ^ 0xffffffff) < -1) {
                        i_67_ += ((i_68_ << 4 ^ i_68_ >>> 5) - -i_68_) ^ is[i_69_ & 0x3] + i_69_;
                        i_69_ += i_70_;
                        i_68_ += (is[(0x1fd8 & i_69_) >>> 11] + i_69_ ^ (i_67_ >>> 5 ^ i_67_ << 4) + i_67_);
                    }
                    this.caret -= 8;
                    writeInt(i_67_);
                    writeInt(i_68_);
                }
                this.caret = i_64_;
                if(bool == true) {
                    break;
                }
                anInt3994 = -83;
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception,
                    ("ji.AC(" + bool + ',' + (is != null ? "{...}" : "null") + ',' + i + ',' + i_63_ + ')'));
            }
            break;
        } while(false);
    }
    
    final void method1237(int i, int i_72_) {
        
        try {
            if(i >= 0 && (i ^ 0xffffffff) > -129) {
                writeByte(i);
            } else {
                if(i_72_ >= -117) {
                    writeByte(-1);
                }
                if(i >= 0 && (i ^ 0xffffffff) > -32769) {
                    writeShort(i + 32768, 1571862888);
                } else {
                    throw new IllegalArgumentException();
                }
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.EC(" + i + ',' + i_72_ + ')');
        }
    }
    
    final int method1239() {
        int i_74_ = 0xff & (this.buffer[this.caret]);
        if((i_74_ ^ 0xffffffff) > -129) {
            return -64 + readUnsignedByte();
        }
        return readUnsignedShort() + -49152;
    }
    
    final int method1240(byte i) {
        
        try {
            if(i != -20) {
                return 50;
            }
            int i_76_ = (this.buffer[this.caret++]);
            int i_77_ = 0;
            for(/**/; i_76_ < 0; i_76_ = (this.buffer[this.caret++])) {
                i_77_ = (i_76_ & 0x7f | i_77_) << 7;
            }
            return i_76_ | i_77_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.NB(" + i + ')');
        }
    }
    
    final int method1242(int i) {// k
        try {
            this.caret += 2;
            int i_79_ = (((this.buffer[-1 + this.caret]) - 128 & 0xff)
                + (0xff00 & ((this.buffer[-2 + this.caret]) << 8)));
            if((i_79_ ^ 0xffffffff) < -32768) {
                i_79_ -= 65536;
            }
            return i_79_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.OC(" + i + ')');
        }
    }
    
    final void method1244(int i, byte i_80_) {
        
        try {
            if(i_80_ != 112) {
                method1217(null, -122, -10, -57);
            }
            this.buffer[this.caret++] = (byte) -i;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.VB(" + i + ',' + i_80_ + ')');
        }
    }
    
    final long method1246(int i) {
        
        try {
            long l = 0xffffffffL & readInt();
            long l_82_ = readInt() & 0xffffffffL;
            if(i >= -87) {
                readShort1((byte) 15);
            }
            return l_82_ + (l << 32);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.N(" + i + ')');
        }
    }
    
    final void method1247(int i, int i_83_) {
        
        try {
            this.buffer[this.caret++] = (byte) i;
            if(i_83_ != 4) {
                method1187((byte) 12);
            }
            this.buffer[this.caret++] = (byte) (i >> 8);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.DA(" + i + ',' + i_83_ + ')');
        }
    }
    
    final long method1248(int i, boolean bool) {
        
        try {
            if(--i < 0 || (i ^ 0xffffffff) < -8) {
                throw new IllegalArgumentException();
            }
            if(bool != false) {
                readIntReverse(false);
            }
            int i_84_ = 8 * i;
            long l = 0L;
            for(/**/; i_84_ >= 0; i_84_ -= 8) {
                l |= ((this.buffer[this.caret++]) & 0xffL) << i_84_;
            }
            return l;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.BB(" + i + ',' + bool + ')');
        }
    }
    
    final void method1250(int i, int i_85_, boolean bool, byte[] is) {
        
        do {
            try {
                for(int i_86_ = i_85_ + i + -1; (i_86_ ^ 0xffffffff) <= (i ^ 0xffffffff); i_86_--) {
                    is[i_86_] = (byte) (-128 + (this.buffer[this.caret++]));
                }
                if(bool == false) {
                    break;
                }
                anInt3994 = -120;
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception,
                    ("ji.NA(" + i + ',' + i_85_ + ',' + bool + ',' + (is != null ? "{...}" : "null") + ')'));
            }
            break;
        } while(false);
    }
    
    final int readByteA(boolean bool) {
        
        try {
            if(bool != true) {
                this.buffer = null;
            }
            return 0xff & (this.buffer[this.caret++]) - 128;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.JA(" + bool + ')');
        }
    }
    
    final int readByteC(byte i) {
        
        try {
            return (-(this.buffer[this.caret++]) & 0xff);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.UA(" + i + ')');
        }
    }
    
    final int readByteS(int i) {
        
        try {
            return (128 + -(this.buffer[this.caret++]) & 0xff);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.OB(" + i + ')');
        }
    }
    
    final int readInt() {
        
        try {
            this.caret += 4;
            return ((0xff & (this.buffer[-1 + this.caret]))
                + ((0xff0000 & ((this.buffer[-3 + this.caret]) << 16))
                + ((0xff & (this.buffer[-4 + this.caret])) << 24))
                + ((this.buffer[-2 + this.caret]) << 8 & 0xff00));
        } catch(RuntimeException runtimeexception) {
            // System.err.println("buffer size: "+this.buffer.length);
            runtimeexception.printStackTrace();
            System.exit(-1);
        }
        throw new RuntimeException();
    }
    
    final int readInt1(boolean bool) {
        
        try {
            this.caret += 4;
            return (((this.buffer[-4 + this.caret]) << 8 & 0xff00)
                + ((0xff0000 & ((this.buffer[this.caret - 1]) << 16))
                + (((this.buffer[this.caret + -2]) & 0xff) << 24))
                - -((this.buffer[-3 + this.caret]) & 0xff));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.M(" + bool + ')');
        }
    }
    
    final int readInt2(int i) {
        
        try {
            this.caret += 4;
            if(i >= -7) {
                this.caret = -81;
            }
            return (((this.buffer[-2 + this.caret]) & 0xff) + (((this.buffer[this.caret + -4]) & 0xff) << 16)
                + ((((this.buffer[-3 + this.caret]) & 0xff) << 24)
                + ((0xff & (this.buffer[this.caret - 1])) << 8)));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.UB(" + i + ')');
        }
    }
    
    final int readIntReverse(boolean bool) {
        
        try {
            this.caret += 4;
            return (((this.buffer[-4 + this.caret]) & 0xff) + ((0xff & (this.buffer[-2 + this.caret])) << 16)
                + ((((this.buffer[-1 + this.caret]) & 0xff) << 24)
                - -((0xff & (this.buffer[-3 + this.caret])) << 8)));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.DC(" + bool + ')');
        }
    }
    
    final int readLEShortA(byte i) {
        
        try {
            this.caret += 2;
            return ((((this.buffer[this.caret - 1]) & 0xff) << 8)
                + (0xff & (this.buffer[-2 + this.caret]) - 128));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.FB(" + i + ')');
        }
    }
    
    final int readShort() {
        this.caret += 2;
        int i = ((0xff00 & ((this.buffer[this.caret - 2]) << 8)) + ((this.buffer[this.caret - 1]) & 0xff));
        if(i > 32767) {
            i -= 65536;
        }
        return i;
    }
    
    final int readShort1(byte i) {
        
        try {
            this.caret += 2;
            return ((0xff & (this.buffer[-2 + this.caret])) + (((this.buffer[-1 + this.caret]) & 0xff) << 8));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.PA(" + i + ')');
        }
    }
    
    final int readShortA(int i) {
        
        try {
            this.caret += 2;
            if(i <= 40) {
                return -92;
            }
            return ((0xff & (this.buffer[this.caret - 1]) - 128)
                + (0xff00 & ((this.buffer[this.caret + -2]) << 8)));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.GC(" + i + ')');
        }
    }
    
    final byte readSignedByte() {
        return this.buffer[this.caret++];
    }
    
    final int readSmart(int i) {
        
        try {
            int i_13_ = 0xff & (this.buffer[this.caret]);
            if(i_13_ < 128) {
                return readUnsignedByte();
            }
            return -32768 + readUnsignedShort();
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.R(" + i + ')');
        }
    }
    
    final int readSmart2() {
        int i_13_ = 0xff & (this.buffer[this.caret]);
        if(i_13_ < 128) {
            return readUnsignedByte() - 1;
        }
        return readUnsignedShort() - 32769;
    }
    
    final String readString() {
        
        try {
            int position = this.caret;
            
            while(((this.buffer[this.caret++]) ^ 0xffffffff) != -1) {
                /* empty */
            }
            int len = -position + this.caret - 1;
            if(len == 0) {
                return "";
            }
            return Node_Sub46_Sub6.method1546(len, position, (byte) -64, (this.buffer));
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.TA(" + +')');
        }
    }
    
    final int readUnsignedByte() {
        
        return ((this.buffer[this.caret++]) & 0xff);
    }
    
    final int readUnsignedShort() {
        this.caret += 2;
        return (((this.buffer[this.caret - 1]) & 0xff) + (((this.buffer[this.caret - 2]) & 0xff) << 8));
    }
    
    final void writeByte(int i) {
        
        try {
            this.buffer[this.caret++] = (byte) i;
        } catch(RuntimeException runtimeexception) {
        }
    }
    
    final void writeByteS(int i, int i_24_) {
        
        do {
            try {
                this.buffer[this.caret++] = (byte) (128 + -i);
                if(i_24_ <= -16) {
                    break;
                }
                method1192((byte) -121);
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception, ("ji.HA(" + i + ',' + i_24_ + ')'));
            }
            break;
        } while(false);
    }
    
    final int writeCrc(int i) {
        int i_12_ = Class365.method3937(this.buffer, this.caret, i);
        writeInt(i_12_);
        return i_12_;
    }
    
    final void writeInt(int i_3_) {
        this.buffer[this.caret++] = (byte) (i_3_ >> 24);
        this.buffer[this.caret++] = (byte) (i_3_ >> 16);
        this.buffer[this.caret++] = (byte) (i_3_ >> 8);
        this.buffer[this.caret++] = (byte) i_3_;
    }
    
    final void writeLEInt(int i, int i_81_) {
        
        try {
            this.buffer[this.caret++] = (byte) i;
            if(i_81_ != 1046032984) {
                readInt();
            }
            this.buffer[this.caret++] = (byte) (i >> 8);
            this.buffer[this.caret++] = (byte) (i >> 16);
            this.buffer[this.caret++] = (byte) (i >> 24);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.TB(" + i + ',' + i_81_ + ')');
        }
    }
    
    final void writeLEShort(int i) {
        this.buffer[this.caret++] = (byte) i;
        this.buffer[this.caret++] = (byte) (i >> 8);
    }
    
    final void writeLEShortA(int i, int i_73_) {
        
        try {
            this.buffer[this.caret++] = (byte) (i_73_ + i);
            this.buffer[this.caret++] = (byte) (i >> 8);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.IC(" + i + ',' + i_73_ + ')');
        }
    }
    
    final void writeRS2String(String string) {
        
        int i_4_ = string.indexOf('\0');
        if((i_4_ ^ 0xffffffff) <= -1) {
            throw new IllegalArgumentException("NUL character at " + i_4_ + " - cannot pjstr");
        }
        
        this.caret += Class200.method2694(string, 0, string.length(), this.caret, (this.buffer), -28439);
        this.buffer[this.caret++] = (byte) 0;
    }
    
    final void writeShort(int i, int i_52_) {
        
        try {
            this.buffer[this.caret++] = (byte) (i >> 8);
            this.buffer[this.caret++] = (byte) i;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.QB(" + i + ',' + i_52_ + ')');
        }
    }
    
    final void writeShortA(int i, byte i_1_) {
        
        try {
            this.buffer[this.caret++] = (byte) (i >> 8);
            if(i_1_ != 126) {
                writeLEInt(75, -5);
            }
            this.buffer[this.caret++] = (byte) (128 + i);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "ji.RA(" + i + ',' + i_1_ + ')');
        }
    }
}
