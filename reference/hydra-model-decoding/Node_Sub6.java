/* Class98_Sub6 - Decompiled by JODE
 * Visit http://jode.sourceforge.net/
 */

final class Node_Sub6 extends Node {
    
    static IncomingOpcode aClass58_3844 = new IncomingOpcode(121, -1);
    static Class79 aClass79_3847 = new Class79(8);
    
    public static void method975(int i) {
        
        do {
            try {
                aClass58_3844 = null;
                aClass79_3847 = null;
                if(i == 1) {
                    break;
                }
                method978(true, true, 19, true, 86);
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception, "cd.B(" + i + ')');
            }
            break;
        } while(false);
    }
    
    static final int method978(boolean bool, boolean bool_5_, int i, boolean bool_6_, int i_7_) {
        
        try {
            Node_Sub3 class98_sub3 = Class64_Sub28.method669(i, bool, 6);
            if(class98_sub3 == null) {
                return 0;
            }
            int i_8_ = 0;
            for(int i_9_ = 0; ((i_9_ ^ 0xffffffff) > (class98_sub3.itemIDS.length ^ 0xffffffff)); i_9_++) {
                if((class98_sub3.itemIDS[i_9_] ^ 0xffffffff) <= -1 && (((Node_Sub46_Sub19.aClass205_6068.anInt1554)
                    ^ 0xffffffff) < (class98_sub3.itemIDS[i_9_] ^ 0xffffffff))) {
                    ItemDefinition itemDefinition = (Node_Sub46_Sub19.aClass205_6068
                        .getItemByID(class98_sub3.itemIDS[i_9_], (byte) -121));
                    int i_10_ = (itemDefinition.method3494(i_7_, (byte) -128,
                        (Node_Sub43_Sub1.aClass365_5897.method3940(i_7_).anInt1202)));
                    if(bool_5_) {
                        i_8_ += (class98_sub3.anIntArray3823[i_9_]) * i_10_;
                    } else {
                        i_8_ += i_10_;
                    }
                }
            }
            if(bool_6_ != true) {
                method975(-10);
            }
            return i_8_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("cd.G(" + bool + ',' + bool_5_ + ',' + i + ',' + bool_6_ + ',' + i_7_ + ')'));
        }
    }
    
    static final Model method981(JS5Archive JS5Archive, int i, int i_16_, int particleId, int toOverride) {
        byte[] is = JS5Archive.getChildFromFolder(i_16_, i);
        if(is == null) {
            return null;
        }
        Model model = new Model(is, i_16_, particleId, toOverride);
        return model;
    }
    
    int anInt3838;
    int anInt3839;
    int anInt3843;
    int anInt3845;
    private int anInt3840;
    private int anInt3841;
    
    private int anInt3842;
    
    private int anInt3846;
    
    private int anInt3848;
    
    Node_Sub6(int i, int i_17_, int i_18_, int i_19_, int i_20_, int i_21_, int i_22_, int i_23_, int i_24_) {
        
        try {
            anInt3842 = i;
            anInt3840 = i_17_;
            this.anInt3845 = i_24_;
            anInt3846 = i_18_;
            anInt3841 = i_20_;
            this.anInt3839 = i_21_;
            anInt3848 = i_19_;
            this.anInt3838 = i_23_;
            this.anInt3843 = i_22_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("cd.<init>(" + i + ',' + i_17_ + ',' + i_18_ + ',' + i_19_
                + ',' + i_20_ + ',' + i_21_ + ',' + i_22_ + ',' + i_23_ + ',' + i_24_ + ')'));
        }
    }
    
    final boolean method976(int i, int i_0_, int i_1_, int i_2_) {
        
        try {
            if(i_0_ < 104) {
                anInt3848 = 122;
            }
            return (anInt3842 ^ 0xffffffff) == (i_2_ ^ 0xffffffff) && i_1_ >= anInt3840 && i_1_ <= anInt3848
                && i >= anInt3846 && i <= anInt3841;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("cd.E(" + i + ',' + i_0_ + ',' + i_1_ + ',' + i_2_ + ')'));
        }
    }
    
    final boolean method977(int i, byte i_3_, int i_4_) {
        
        try {
            if(i_3_ <= 32) {
                anInt3848 = 89;
            }
            return ((this.anInt3839 ^ 0xffffffff) >= (i ^ 0xffffffff)) && i <= this.anInt3838 && this.anInt3843 <= i_4_
                && this.anInt3845 >= i_4_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("cd.D(" + i + ',' + i_3_ + ',' + i_4_ + ')'));
        }
    }
    
    final void method979(int i, int i_11_, int i_12_, int[] is) {
        
        try {
            is[1] = i_12_ - this.anInt3839 - -anInt3840;
            is[i_11_] = anInt3842;
            is[2] = i - -anInt3846 + -this.anInt3843;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("cd.H(" + i + ',' + i_11_ + ',' + i_12_ + ',' + (is != null ? "{...}" : "null") + ')'));
        }
    }
    
    final boolean method980(int i, int i_13_, int i_14_) {
        
        try {
            if(i > -94) {
                method980(104, -50, -22);
            }
            return (i_13_ ^ 0xffffffff) <= (anInt3840 ^ 0xffffffff) && (i_13_ ^ 0xffffffff) >= (anInt3848 ^ 0xffffffff)
                && (anInt3846 ^ 0xffffffff) >= (i_14_ ^ 0xffffffff) && anInt3841 >= i_14_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("cd.C(" + i + ',' + i_13_ + ',' + i_14_ + ')'));
        }
    }
    
    final void method982(int i, int i_25_, int i_26_, int[] is) {
        
        do {
            try {
                is[0] = 0;
                is[2] = this.anInt3843 - anInt3846 + i;
                is[1] = -anInt3840 - (-this.anInt3839 - i_25_);
                if(i_26_ > 43) {
                    break;
                }
                anInt3842 = 27;
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception,
                    ("cd.A(" + i + ',' + i_25_ + ',' + i_26_ + ',' + (is != null ? "{...}" : "null") + ')'));
            }
            break;
        } while(false);
    }
}
