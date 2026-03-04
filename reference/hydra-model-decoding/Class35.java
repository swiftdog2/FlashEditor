
/* Class35 - Decompiled by JODE
 * Visit http://jode.sourceforge.net/
 */

import java.io.File;

final class Class35 {
    
    static EntityEnumType aEntity_EnumType_332;
    static int[] anIntArray333;
    static String[] aStringArray335 = new String[5];
    static int anInt336 = 0;
    
    static {
        aEntity_EnumType_332 = new EntityEnumType(7, 19);
    }
    
    static final int method332(int i) {
        
        try {
            if(i != 2) {
                method334(-6);
            }
            if(Class2.anIntArray70 == null) {
                return 0;
            }
            return Class2.anIntArray70.length * 2;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "cia.E(" + i + ')');
        }
    }
    
    static final void method333(Particle_Sub1 class246_sub1, int i) {
        
        try {
            int i_0_ = 120 / ((-58 - i) / 59);
            class246_sub1.aClass246_Sub3_5069 = null;
            int i_1_ = (class246_sub1.aClass246_Sub6Array5067).length;
            for(int i_2_ = 0; (i_1_ ^ 0xffffffff) < (i_2_ ^ 0xffffffff); i_2_++) {
                class246_sub1.aClass246_Sub6Array5067[i_2_].aBoolean5114 = false;
            }
            synchronized(Node_Sub46_Sub20_Sub2.aClass218Array6316) {
                if(Node_Sub46_Sub20_Sub2.aClass218Array6316.length > i_1_
                    && (Class1.anIntArray65[i_1_] ^ 0xffffffff) > -201) {
                    Node_Sub46_Sub20_Sub2.aClass218Array6316[i_1_].method2808(true, class246_sub1);
                    Class1.anIntArray65[i_1_]++;
                }
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("cia.A(" + (class246_sub1 != null ? "{...}" : "null") + ',' + i + ')'));
        }
    }
    
    public static void method334(int i) {
        
        try {
            if(i > -103) {
                aStringArray335 = null;
            }
            aEntity_EnumType_332 = null;
            anIntArray333 = null;
            aStringArray335 = null;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "cia.C(" + i + ')');
        }
    }
    
    static long method335(long l, long l_3_) {
        
        try {
            return l & l_3_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "cia.H(" + l + ',' + l_3_ + ')');
        }
    }
    
    static final int method337(int i, String string) {
        
        try {
            if(!Class242.aSignlink_1848.aBoolean682) {
                return -1;
            }
            if(Class124.aHashtable1015.containsKey(string)) {
                return 100;
            }
            String string_5_ = Class145.method2315(79, string);
            if(string_5_ == null) {
                return -1;
            }
            String string_6_ = Class343.aString2863 + string_5_;
            if(!Class223.aJS5Archive_1681.method2737(true, "", string_6_)) {
                return -1;
            }
            if(!Class223.aJS5Archive_1681.method2741(string_6_, 0)) {
                return Class223.aJS5Archive_1681.method2748(29952, string_6_);
            }
            if(i > -28) {
                anIntArray333 = null;
            }
            byte[] is = Class223.aJS5Archive_1681.method2739(string_6_, "", -32734);
            Object object = null;
            File file;
            try {
                file = Class316.method3649(string_5_, -127);
                
            } catch(RuntimeException runtimeexception) {
                runtimeexception.printStackTrace();
                return -1;
            }
            if(is != null && file != null) {
                boolean bool = true;
                byte[] is_7_ = Class273.method3281(-67, file);
                if(is_7_ != null && is_7_.length == is.length) {
                    for(int i_8_ = 0; (i_8_ ^ 0xffffffff) > (is_7_.length ^ 0xffffffff); i_8_++) {
                        if(is[i_8_] != is_7_[i_8_]) {
                            bool = false;
                            break;
                        }
                    }
                } else {
                    bool = false;
                }
                try {
                    if(!bool) {
                        Class242.aSignlink_1848.method876(is, true, file);
                    }
                } catch(Throwable throwable) {
                    throwable.printStackTrace();
                    return -1;
                }
                Class221.method2821(file, string, -320);
                return 100;
            }
            return -1;
        } catch(RuntimeException runtimeexception) {
            runtimeexception.printStackTrace();
            throw Class64_Sub27.method667(runtimeexception,
                ("cia.G(" + i + ',' + (string != null ? "{...}" : "null") + ')'));
        }
    }
    
    static final int method338(int i, int i_9_, int i_10_, int i_11_, int i_12_, int i_13_, byte i_14_) {
        
        try {
            i_9_ &= 0x3;
            if(i_14_ != -23) {
                return 76;
            }
            if((i_10_ & 0x1) == 1) {
                int i_15_ = i_13_;
                i_13_ = i;
                i = i_15_;
            }
            if(i_9_ == 0) {
                return i_12_;
            }
            if((i_9_ ^ 0xffffffff) == -2) {
                return 1 - i_13_ + (-i_11_ + 7);
            }
            if((i_9_ ^ 0xffffffff) == -3) {
                return -i_12_ + 7 - i + 1;
            }
            return i_11_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("cia.F(" + i + ',' + i_9_ + ',' + i_10_ + ',' + i_11_ + ','
                + i_12_ + ',' + i_13_ + ',' + i_14_ + ')'));
        }
    }
    
    int anInt327;
    
    Class35 aClass35_328;
    
    int anInt329;
    
    int anInt330;
    
    int anInt331;
    
    Class111 aClass111_334;
    
    int anInt337;
    
    Class35(int i, int i_16_) {
        
        try {
            this.anInt329 = i;
            this.anInt327 = i_16_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("cia.<init>(" + i + ',' + i_16_ + ')'));
        }
    }
    
    final Class66 method331(byte i) {
        
        try {
            if(i < 89) {
                return null;
            }
            return Class21.method263(this.anInt329, 31866);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "cia.D(" + i + ')');
        }
    }
    
    final Class35 method336(int i, int i_4_) {
        
        try {
            if(i != -1854) {
                method331((byte) -2);
            }
            return new Class35(this.anInt329, i_4_);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "cia.B(" + i + ',' + i_4_ + ')');
        }
    }
}
