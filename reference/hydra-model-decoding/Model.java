
/* Class178 - Decompiled by JODE
 * Visit http://jode.sourceforge.net/
 */

import java.util.ArrayList;
import java.util.List;

final class Model {
    
    static boolean aBoolean1401 = false;
    static int anInt1405 = 0;
    
    static final float method2588(float f, int i, int i_49_, float f_50_, float f_51_) {
        
        try {
            if(i_49_ != -24576) {
                method2588(-0.72166127F, 92, -119, -1.0185089F, -1.6095228F);
            }
            float[] fs = Class48_Sub2_Sub1.aFloatArrayArray5522[i];
            return fs[2] * f_51_ + (f * fs[0] + fs[1] * f_50_);
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("lv.D(" + f + ',' + i + ',' + i_49_ + ',' + f_50_ + ',' + f_51_ + ')'));
        }
    }
    
    short[] aShortArray1385;
    int[] anIntArray1386;
    int formatType = 12;
    byte[] aByteArray1388;
    int[] anIntArray1389;
    int[] anIntArray1390;
    int triangles;
    short[] aShortArray1392;
    short[] aShortArray1393;
    short[] aShortArray1394;
    int[] anIntArray1395;
    int texturedTriangles;
    int[] anIntArray1397;
    Class35[] aClass35Array1398;
    byte[] aByteArray1399;
    int[] anIntArray1408;
    byte[] aByteArray1402;
    short[] aShortArray1403;
    int[] anIntArray1404;
    int anInt1406;
    int vertices = 0;
    short[] aShortArray1408;
    short[] aShortArray1409;
    short[] aShortArray1410;
    byte[] aByteArray1411;
    int[] anIntArray1412;
    ModelParticle[] modelParticles;
    byte[] aByteArray1414;
    short[] aShortArray1415;
    int[] anIntArray1407;
    int[] anIntArray1411;
    int[] anIntArray1409;
    Class106[] aClass106Array1419;
    byte[] aByteArray1420;
    short[] aShortArray1421;
    byte aByte1422;
    
    byte[] aByteArray1423;
    
    private int modelID;
    public int customParticleId = -1;
    public int toOverrideParticleId = -1;
    public boolean newProtocol;
    public int anInt1413;
    
    public Model() {
        
        this.triangles = 0;
        this.aByte1422 = (byte) 0;
        this.anInt1406 = 0;
        this.texturedTriangles = 0;
    }
    
    Model(byte[] is, int id, int particleId, int toOverride) {
        this.toOverrideParticleId = toOverride;
        this.customParticleId = particleId;
        this.modelID = id;
        this.triangles = 0;
        this.aByte1422 = (byte) 0;
        this.anInt1406 = 0;
        this.texturedTriangles = 0;
        
        if(modelID >= 63607 && modelID <= 63613) {
            newProtocol = true;
            decoder_newest_format(is);
            return;
        }
        
        try {
            if(is[-1 + is.length] == -1 && is[is.length + -2] == -1) {
                decoder_newer_format(is, 1);
            } else {
                method2587(is, -1);
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "lv.<init>(" + (is != null ? "{...}" : "null") + ')');
        }
        
    }
    
    Model(int i, int i_212_, int i_213_) {
        
        this.triangles = 0;
        this.aByte1422 = (byte) 0;
        this.anInt1406 = 0;
        this.texturedTriangles = 0;
        try {
            this.aByteArray1420 = new byte[i_212_];
            this.anIntArray1407 = new int[i];
            this.aByteArray1402 = new byte[i_212_];
            this.anIntArray1409 = new int[i];
            this.anIntArray1408 = new int[i];
            this.aByteArray1414 = new byte[i_212_];
            this.anIntArray1395 = new int[i_212_];
            this.aShortArray1415 = new short[i_212_];
            this.aShortArray1410 = new short[i_212_];
            this.aShortArray1393 = new short[i_212_];
            this.anIntArray1411 = new int[i];
            this.aByteArray1411 = new byte[i_212_];
            if(i_213_ > 0) {
                this.aByteArray1399 = new byte[i_213_];
                this.aByteArray1423 = new byte[i_213_];
                this.anIntArray1412 = new int[i_213_];
                this.aShortArray1385 = new short[i_213_];
                this.anIntArray1386 = new int[i_213_];
                this.anIntArray1390 = new int[i_213_];
                this.anIntArray1397 = new int[i_213_];
                this.aByteArray1388 = new byte[i_213_];
                this.anIntArray1404 = new int[i_213_];
                this.aShortArray1421 = new short[i_213_];
                this.anIntArray1389 = new int[i_213_];
                this.aShortArray1403 = new short[i_213_];
            }
            this.aShortArray1409 = new short[i_212_];
            this.aShortArray1392 = new short[i_212_];
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("lv.<init>(" + i + ',' + i_212_ + ',' + i_213_ + ')'));
        }
    }
    
    Model(Model[] models, int i) {
        
        this.triangles = 0;
        this.aByte1422 = (byte) 0;
        this.anInt1406 = 0;
        this.texturedTriangles = 0;
        try {
            this.vertices = 0;
            this.triangles = 0;
            this.texturedTriangles = 0;
            int i_214_ = 0;
            int i_215_ = 0;
            int i_216_ = 0;
            boolean bool = false;
            boolean bool_217_ = false;
            boolean bool_218_ = false;
            boolean bool_219_ = false;
            boolean bool_220_ = false;
            this.aByte1422 = (byte) -1;
            boolean bool_221_ = false;
            for(int i_222_ = 0; i_222_ < i; i_222_++) {
                Model model_223_ = models[i_222_];
                if(model_223_ != null) {
                    customParticleId = model_223_.customParticleId;
                    this.triangles += model_223_.triangles;
                    this.vertices += model_223_.vertices;
                    this.texturedTriangles += model_223_.texturedTriangles;
                    if(model_223_.modelParticles != null) {
                        i_214_ += (model_223_.modelParticles).length;
                    }
                    if(model_223_.aClass106Array1419 != null) {
                        i_216_ += (model_223_.aClass106Array1419).length;
                    }
                    bool = bool | (model_223_.aByteArray1414 != null);
                    if(model_223_.aClass35Array1398 != null) {
                        i_215_ += (model_223_.aClass35Array1398).length;
                    }
                    bool_220_ = bool_220_ | (model_223_.aShortArray1409) != null;
                    bool_219_ = bool_219_ | (model_223_.aByteArray1420) != null;
                    bool_218_ = bool_218_ | (model_223_.aByteArray1411) != null;
                    if(model_223_.aByteArray1402 != null) {
                        bool_217_ = true;
                    } else {
                        if((this.aByte1422 ^ 0xffffffff) == 0) {
                            this.aByte1422 = model_223_.aByte1422;
                        }
                        if((this.aByte1422 ^ 0xffffffff) != (model_223_.aByte1422 ^ 0xffffffff)) {
                            bool_217_ = true;
                        }
                    }
                    bool_221_ = bool_221_ | (model_223_.anIntArray1395) != null;
                }
            }
            this.aShortArray1410 = new short[this.triangles];
            this.aShortArray1392 = new short[this.triangles];
            this.anIntArray1409 = new int[this.vertices];
            if(bool_221_) {
                this.anIntArray1395 = new int[this.triangles];
            }
            this.anIntArray1408 = new int[this.vertices];
            if((i_215_ ^ 0xffffffff) < -1) {
                this.aClass35Array1398 = new Class35[i_215_];
            }
            if(bool_218_) {
                this.aByteArray1411 = new byte[this.triangles];
            }
            this.anIntArray1411 = new int[this.vertices];
            this.aShortArray1394 = new short[this.triangles];
            if(bool_220_) {
                this.aShortArray1409 = new short[this.triangles];
            }
            if(bool_217_) {
                this.aByteArray1402 = new byte[this.triangles];
            }
            if((this.texturedTriangles ^ 0xffffffff) < -1) {
                this.aShortArray1403 = new short[this.texturedTriangles];
                this.aByteArray1388 = new byte[this.texturedTriangles];
                this.aByteArray1399 = new byte[this.texturedTriangles];
                this.anIntArray1390 = new int[this.texturedTriangles];
                this.aByteArray1423 = new byte[this.texturedTriangles];
                this.aShortArray1385 = new short[this.texturedTriangles];
                this.anIntArray1386 = new int[this.texturedTriangles];
                this.anIntArray1397 = new int[this.texturedTriangles];
                this.aShortArray1421 = new short[this.texturedTriangles];
                this.anIntArray1412 = new int[this.texturedTriangles];
                this.anIntArray1389 = new int[this.texturedTriangles];
                this.anIntArray1404 = new int[this.texturedTriangles];
            }
            if(i_214_ > 0) {
                this.modelParticles = new ModelParticle[i_214_];
            }
            if(bool_219_) {
                this.aByteArray1420 = new byte[this.triangles];
            }
            this.aShortArray1408 = new short[this.vertices];
            this.anIntArray1407 = new int[this.vertices];
            this.aShortArray1415 = new short[this.triangles];
            if(bool) {
                this.aByteArray1414 = new byte[this.triangles];
            }
            this.aShortArray1393 = new short[this.triangles];
            if(i_216_ > 0) {
                this.aClass106Array1419 = new Class106[i_216_];
            }
            this.vertices = 0;
            this.texturedTriangles = 0;
            i_214_ = 0;
            i_216_ = 0;
            this.triangles = 0;
            i_215_ = 0;
            for(int i_224_ = 0; i > i_224_; i_224_++) {
                short i_225_ = (short) (1 << i_224_);
                Model model_226_ = models[i_224_];
                if(model_226_ != null) {
                    if(model_226_.aClass106Array1419 != null) {
                        for(int i_227_ = 0; (((model_226_.aClass106Array1419).length ^ 0xffffffff) < (i_227_
                            ^ 0xffffffff)); i_227_++) {
                            Class106 class106 = (model_226_.aClass106Array1419[i_227_]);
                            this.aClass106Array1419[i_216_++] = class106
                                .method1719(((this.triangles) + (class106.anInt906)), -125);
                        }
                    }
                    for(int i_228_ = 0; ((i_228_ ^ 0xffffffff) > (model_226_.triangles ^ 0xffffffff)); i_228_++) {
                        if(bool && (model_226_.aByteArray1414 != null)) {
                            this.aByteArray1414[(this.triangles)] = (model_226_.aByteArray1414[i_228_]);
                        }
                        if(bool_217_) {
                            if(model_226_.aByteArray1402 != null) {
                                this.aByteArray1402[this.triangles] = (model_226_.aByteArray1402[i_228_]);
                            } else {
                                this.aByteArray1402[this.triangles] = model_226_.aByte1422;
                            }
                        }
                        if(bool_218_ && (model_226_.aByteArray1411 != null)) {
                            this.aByteArray1411[(this.triangles)] = (model_226_.aByteArray1411[i_228_]);
                        }
                        if(bool_220_) {
                            if(model_226_.aShortArray1409 == null) {
                                this.aShortArray1409[this.triangles] = (short) -1;
                            } else {
                                this.aShortArray1409[this.triangles] = (model_226_.aShortArray1409[i_228_]);
                            }
                        }
                        if(bool_221_) {
                            if(model_226_.anIntArray1395 != null) {
                                this.anIntArray1395[this.triangles] = (model_226_.anIntArray1395[i_228_]);
                            } else {
                                this.anIntArray1395[this.triangles] = -1;
                            }
                        }
                        this.aShortArray1393[(this.triangles)] = (short) method2598(model_226_,
                            (model_226_.aShortArray1393[i_228_]), i_225_, 0);
                        this.aShortArray1410[(this.triangles)] = (short) method2598(model_226_,
                            (model_226_.aShortArray1410[i_228_]), i_225_, 0);
                        this.aShortArray1392[(this.triangles)] = (short) method2598(model_226_,
                            (model_226_.aShortArray1392[i_228_]), i_225_, 0);
                        this.aShortArray1394[(this.triangles)] = i_225_;
                        this.aShortArray1415[(this.triangles)] = (model_226_.aShortArray1415[i_228_]);
                        this.triangles++;
                    }
                    if(model_226_.modelParticles != null) {
                        for(int i_229_ = 0; ((model_226_.modelParticles).length > i_229_); i_229_++) {
                            int i_230_ = method2598(model_226_, (model_226_.modelParticles[i_229_].anInt666), i_225_,
                                0);
                            int i_231_ = method2598(model_226_, (model_226_.modelParticles[i_229_].anInt661), i_225_,
                                0);
                            int i_232_ = method2598(model_226_, (model_226_.modelParticles[i_229_].anInt674), i_225_,
                                0);
                            this.modelParticles[i_214_] = model_226_.modelParticles[i_229_].method857(i_230_, true,
                                i_232_, i_231_);
                            i_214_++;
                        }
                    }
                    if(model_226_.aClass35Array1398 != null) {
                        for(int i_233_ = 0; ((i_233_ ^ 0xffffffff) > ((model_226_.aClass35Array1398).length
                            ^ 0xffffffff)); i_233_++) {
                            int i_234_ = method2598(model_226_, (model_226_.aClass35Array1398[i_233_].anInt327), i_225_,
                                0);
                            this.aClass35Array1398[i_215_] = model_226_.aClass35Array1398[i_233_].method336(-1854,
                                i_234_);
                            i_215_++;
                        }
                    }
                }
            }
            int i_235_ = 0;
            this.anInt1406 = this.vertices;
            for(int i_236_ = 0; i > i_236_; i_236_++) {
                short i_237_ = (short) (1 << i_236_);
                Model model_238_ = models[i_236_];
                if(model_238_ != null) {
                    for(int i_239_ = 0; model_238_.triangles > i_239_; i_239_++) {
                        if(bool_219_) {
                            this.aByteArray1420[i_235_++] = (byte) (((model_238_.aByteArray1420) != null
                                && (model_238_.aByteArray1420[i_239_]) != -1)
                                ? ((model_238_.aByteArray1420[i_239_]) + this.texturedTriangles) : -1);
                        }
                    }
                    for(int i_240_ = 0; i_240_ < model_238_.texturedTriangles; i_240_++) {
                        byte i_241_ = (this.aByteArray1388[this.texturedTriangles] = (model_238_.aByteArray1388[i_240_]));
                        if((i_241_ ^ 0xffffffff) == -1) {
                            this.aShortArray1403[this.texturedTriangles] = (short) method2598(model_238_,
                                (model_238_.aShortArray1403[i_240_]), i_237_, 0);
                            this.aShortArray1421[this.texturedTriangles] = (short) method2598(model_238_,
                                (model_238_.aShortArray1421[i_240_]), i_237_, 0);
                            this.aShortArray1385[this.texturedTriangles] = (short) method2598(model_238_,
                                (model_238_.aShortArray1385[i_240_]), i_237_, 0);
                        }
                        if((i_241_ ^ 0xffffffff) <= -2 && (i_241_ ^ 0xffffffff) >= -4) {
                            this.aShortArray1403[this.texturedTriangles] = (model_238_.aShortArray1403[i_240_]);
                            this.aShortArray1421[this.texturedTriangles] = (model_238_.aShortArray1421[i_240_]);
                            this.aShortArray1385[this.texturedTriangles] = (model_238_.aShortArray1385[i_240_]);
                            this.anIntArray1389[(this.texturedTriangles)] = (model_238_.anIntArray1389[i_240_]);
                            this.anIntArray1404[(this.texturedTriangles)] = (model_238_.anIntArray1404[i_240_]);
                            this.anIntArray1390[(this.texturedTriangles)] = (model_238_.anIntArray1390[i_240_]);
                            this.aByteArray1423[(this.texturedTriangles)] = (model_238_.aByteArray1423[i_240_]);
                            this.aByteArray1399[(this.texturedTriangles)] = (model_238_.aByteArray1399[i_240_]);
                            this.anIntArray1412[(this.texturedTriangles)] = (model_238_.anIntArray1412[i_240_]);
                        }
                        if((i_241_ ^ 0xffffffff) == -3) {
                            this.anIntArray1397[(this.texturedTriangles)] = (model_238_.anIntArray1397[i_240_]);
                            this.anIntArray1386[(this.texturedTriangles)] = (model_238_.anIntArray1386[i_240_]);
                        }
                        this.texturedTriangles++;
                    }
                }
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("lv.<init>(" + (models != null ? "{...}" : "null") + ',' + i + ')'));
        }
    }
    
    private final void decoder_newer_format(byte[] is, int i) {
        
        do {
            try {
                RSBuffer RSBuffer = new RSBuffer(is);
                RSBuffer RSBuffer_52_ = new RSBuffer(is);
                RSBuffer RSBuffer_53_ = new RSBuffer(is);
                RSBuffer RSBuffer_54_ = new RSBuffer(is);
                RSBuffer RSBuffer_55_ = new RSBuffer(is);
                RSBuffer RSBuffer_56_ = new RSBuffer(is);
                RSBuffer RSBuffer_57_ = new RSBuffer(is);
                RSBuffer.caret = is.length - 23;
                this.vertices = RSBuffer.readUnsignedShort();
                this.triangles = RSBuffer.readUnsignedShort();
                this.texturedTriangles = RSBuffer.readUnsignedByte();
                int i_58_ = RSBuffer.readUnsignedByte();
                boolean bool = (i_58_ & i) == 1;
                boolean bool_59_ = (0x2 & i_58_ ^ 0xffffffff) == -3;
                boolean bool_60_ = (i_58_ & 0x4) == 4;
                boolean bool_61_ = (i_58_ & 0x8 ^ 0xffffffff) == -9;
                if(bool_61_) {
                    RSBuffer.caret -= 7;
                    this.formatType = RSBuffer.readUnsignedByte();
                    RSBuffer.caret += 6;
                }
                int i_62_ = RSBuffer.readUnsignedByte();
                int i_63_ = RSBuffer.readUnsignedByte();
                int i_64_ = RSBuffer.readUnsignedByte();
                int i_65_ = RSBuffer.readUnsignedByte();
                int i_66_ = RSBuffer.readUnsignedByte();
                int i_67_ = RSBuffer.readUnsignedShort();
                int i_68_ = RSBuffer.readUnsignedShort();
                int i_69_ = RSBuffer.readUnsignedShort();
                int i_70_ = RSBuffer.readUnsignedShort();
                int i_71_ = RSBuffer.readUnsignedShort();
                int i_72_ = 0;
                int i_73_ = 0;
                int i_74_ = 0;
                if(this.texturedTriangles > 0) {
                    RSBuffer.caret = 0;
                    this.aByteArray1388 = new byte[this.texturedTriangles];
                    for(int i_75_ = 0; i_75_ < this.texturedTriangles; i_75_++) {
                        byte i_76_ = (this.aByteArray1388[i_75_] = RSBuffer.readSignedByte());
                        if((i_76_ ^ 0xffffffff) == -3) {
                            i_74_++;
                        }
                        if((i_76_ ^ 0xffffffff) == -1) {
                            i_72_++;
                        }
                        if((i_76_ ^ 0xffffffff) <= -2 && i_76_ <= 3) {
                            i_73_++;
                        }
                    }
                }
                int i_77_ = this.texturedTriangles;
                int i_78_ = i_77_;
                i_77_ += this.vertices;
                int i_79_ = i_77_;
                if(bool) {
                    i_77_ += this.triangles;
                }
                int i_80_ = i_77_;
                i_77_ += this.triangles;
                int i_81_ = i_77_;
                if(i_62_ == 255) {
                    i_77_ += this.triangles;
                }
                int i_82_ = i_77_;
                if(i_64_ == 1) {
                    i_77_ += this.triangles;
                }
                int i_83_ = i_77_;
                if(i_66_ == 1) {
                    i_77_ += this.vertices;
                }
                int i_84_ = i_77_;
                if(i_63_ == 1) {
                    i_77_ += this.triangles;
                }
                int i_85_ = i_77_;
                i_77_ += i_70_;
                int i_86_ = i_77_;
                if(i_65_ == 1) {
                    i_77_ += 2 * this.triangles;
                }
                int i_87_ = i_77_;
                i_77_ += i_71_;
                int i_88_ = i_77_;
                i_77_ += 2 * this.triangles;
                int i_89_ = i_77_;
                i_77_ += i_67_;
                int i_90_ = i_77_;
                i_77_ += i_68_;
                int i_91_ = i_77_;
                i_77_ += i_69_;
                int i_92_ = i_77_;
                i_77_ += i_72_ * 6;
                int i_93_ = i_77_;
                i_77_ += i_73_ * 6;
                int i_94_ = 6;
                if((this.formatType ^ 0xffffffff) != -15) {
                    if((this.formatType ^ 0xffffffff) <= -16) {
                        i_94_ = 9;
                    }
                } else {
                    i_94_ = 7;
                }
                int i_95_ = i_77_;
                i_77_ += i_73_ * i_94_;
                int i_96_ = i_77_;
                i_77_ += i_73_;
                int i_97_ = i_77_;
                i_77_ += i_73_;
                int i_98_ = i_77_;
                i_77_ += 2 * i_74_ + i_73_;
                this.aShortArray1410 = new short[this.triangles];
                this.aShortArray1393 = new short[this.triangles];
                int i_99_ = i_77_;
                if((i_64_ ^ 0xffffffff) == -2) {
                    this.anIntArray1395 = new int[this.triangles];
                }
                if(bool) {
                    this.aByteArray1414 = new byte[this.triangles];
                }
                if((i_63_ ^ 0xffffffff) == -2) {
                    this.aByteArray1411 = new byte[this.triangles];
                }
                RSBuffer.caret = i_78_;
                if((this.texturedTriangles ^ 0xffffffff) < -1) {
                    if((i_73_ ^ 0xffffffff) < -1) {
                        this.anIntArray1412 = new int[i_73_];
                        this.anIntArray1389 = new int[i_73_];
                        this.aByteArray1423 = new byte[i_73_];
                        this.aByteArray1399 = new byte[i_73_];
                        this.anIntArray1404 = new int[i_73_];
                        this.anIntArray1390 = new int[i_73_];
                    }
                    if((i_74_ ^ 0xffffffff) < -1) {
                        this.anIntArray1397 = new int[i_74_];
                        this.anIntArray1386 = new int[i_74_];
                    }
                    this.aShortArray1403 = new short[this.texturedTriangles];
                    this.aShortArray1421 = new short[this.texturedTriangles];
                    this.aShortArray1385 = new short[this.texturedTriangles];
                }
                this.anIntArray1407 = new int[this.vertices];
                if(i_65_ == 1) {
                    this.aShortArray1409 = new short[this.triangles];
                }
                this.aShortArray1392 = new short[this.triangles];
                this.aShortArray1415 = new short[this.triangles];
                if((i_62_ ^ 0xffffffff) == -256) {
                    this.aByteArray1402 = new byte[this.triangles];
                } else {
                    this.aByte1422 = (byte) i_62_;
                }
                this.anIntArray1408 = new int[this.vertices];
                this.anIntArray1409 = new int[this.vertices];
                if(i_65_ == 1 && (this.texturedTriangles ^ 0xffffffff) < -1) {
                    this.aByteArray1420 = new byte[this.triangles];
                }
                if((i_66_ ^ 0xffffffff) == -2) {
                    this.anIntArray1411 = new int[this.vertices];
                }
                RSBuffer_52_.caret = i_89_;
                RSBuffer_53_.caret = i_90_;
                RSBuffer_54_.caret = i_91_;
                RSBuffer_55_.caret = i_83_;
                int i_100_ = 0;
                int i_101_ = 0;
                int i_102_ = 0;
                for(int i_103_ = 0; ((this.vertices ^ 0xffffffff) < (i_103_ ^ 0xffffffff)); i_103_++) {
                    int i_104_ = RSBuffer.readUnsignedByte();
                    int i_105_ = 0;
                    if((0x1 & i_104_ ^ 0xffffffff) != -1) {
                        i_105_ = RSBuffer_52_.method1239();
                    }
                    int i_106_ = 0;
                    if((i_104_ & 0x2 ^ 0xffffffff) != -1) {
                        i_106_ = RSBuffer_53_.method1239();
                    }
                    int i_107_ = 0;
                    if((0x4 & i_104_) != 0) {
                        i_107_ = RSBuffer_54_.method1239();
                    }
                    this.anIntArray1407[i_103_] = i_100_ + i_105_;
                    this.anIntArray1408[i_103_] = i_101_ + i_106_;
                    this.anIntArray1409[i_103_] = i_102_ - -i_107_;
                    i_101_ = this.anIntArray1408[i_103_];
                    i_102_ = this.anIntArray1409[i_103_];
                    i_100_ = this.anIntArray1407[i_103_];
                    if((i_66_ ^ 0xffffffff) == -2) {
                        this.anIntArray1411[i_103_] = RSBuffer_55_.readUnsignedByte();
                    }
                }
                RSBuffer.caret = i_88_;
                RSBuffer_52_.caret = i_79_;
                RSBuffer_53_.caret = i_81_;
                RSBuffer_54_.caret = i_84_;
                RSBuffer_55_.caret = i_82_;
                RSBuffer_56_.caret = i_86_;
                RSBuffer_57_.caret = i_87_;
                for(int i_108_ = 0; this.triangles > i_108_; i_108_++) {
                    this.aShortArray1415[i_108_] = (short) RSBuffer.readUnsignedShort();
                    if(bool) {
                        this.aByteArray1414[i_108_] = RSBuffer_52_.readSignedByte();
                    }
                    if(i_62_ == 255) {
                        this.aByteArray1402[i_108_] = RSBuffer_53_.readSignedByte();
                    }
                    if((i_63_ ^ 0xffffffff) == -2) {
                        this.aByteArray1411[i_108_] = RSBuffer_54_.readSignedByte();
                    }
                    if(i_64_ == 1) {
                        this.anIntArray1395[i_108_] = RSBuffer_55_.readUnsignedByte();
                    }
                    if(i_65_ == 1) {
                        this.aShortArray1409[i_108_] = (short) (RSBuffer_56_.readUnsignedShort() + -1);
                    }
                    if(this.aByteArray1420 != null) {
                        if(this.aShortArray1409[i_108_] == -1) {
                            this.aByteArray1420[i_108_] = (byte) -1;
                        } else {
                            this.aByteArray1420[i_108_] = (byte) (RSBuffer_57_.readUnsignedByte() + -1);
                        }
                    }
                }
                this.anInt1406 = -1;
                RSBuffer.caret = i_85_;
                RSBuffer_52_.caret = i_80_;
                short i_109_ = 0;
                short i_110_ = 0;
                short i_111_ = 0;
                int i_112_ = 0;
                for(int i_113_ = 0; i_113_ < this.triangles; i_113_++) {
                    int i_114_ = RSBuffer_52_.readUnsignedByte();
                    if((i_114_ ^ 0xffffffff) == -2) {
                        i_109_ = (short) (i_112_ + RSBuffer.method1239());
                        i_112_ = i_109_;
                        i_110_ = (short) (RSBuffer.method1239() + i_112_);
                        i_112_ = i_110_;
                        i_111_ = (short) (RSBuffer.method1239() + i_112_);
                        i_112_ = i_111_;
                        this.aShortArray1393[i_113_] = i_109_;
                        this.aShortArray1410[i_113_] = i_110_;
                        this.aShortArray1392[i_113_] = i_111_;
                        if(i_109_ > this.anInt1406) {
                            this.anInt1406 = i_109_;
                        }
                        if(this.anInt1406 < i_110_) {
                            this.anInt1406 = i_110_;
                        }
                        if(i_111_ > this.anInt1406) {
                            this.anInt1406 = i_111_;
                        }
                    }
                    if((i_114_ ^ 0xffffffff) == -3) {
                        i_110_ = i_111_;
                        i_111_ = (short) (i_112_ + RSBuffer.method1239());
                        this.aShortArray1393[i_113_] = i_109_;
                        i_112_ = i_111_;
                        this.aShortArray1410[i_113_] = i_110_;
                        this.aShortArray1392[i_113_] = i_111_;
                        if((i_111_ ^ 0xffffffff) < (this.anInt1406 ^ 0xffffffff)) {
                            this.anInt1406 = i_111_;
                        }
                    }
                    if((i_114_ ^ 0xffffffff) == -4) {
                        i_109_ = i_111_;
                        i_111_ = (short) (i_112_ + RSBuffer.method1239());
                        i_112_ = i_111_;
                        this.aShortArray1393[i_113_] = i_109_;
                        this.aShortArray1410[i_113_] = i_110_;
                        this.aShortArray1392[i_113_] = i_111_;
                        if((this.anInt1406 ^ 0xffffffff) > (i_111_ ^ 0xffffffff)) {
                            this.anInt1406 = i_111_;
                        }
                    }
                    if((i_114_ ^ 0xffffffff) == -5) {
                        short i_115_ = i_109_;
                        i_109_ = i_110_;
                        i_110_ = i_115_;
                        i_111_ = (short) (i_112_ + RSBuffer.method1239());
                        this.aShortArray1393[i_113_] = i_109_;
                        i_112_ = i_111_;
                        this.aShortArray1410[i_113_] = i_110_;
                        this.aShortArray1392[i_113_] = i_111_;
                        if((this.anInt1406 ^ 0xffffffff) > (i_111_ ^ 0xffffffff)) {
                            this.anInt1406 = i_111_;
                        }
                    }
                }
                this.anInt1406++;
                RSBuffer.caret = i_92_;
                RSBuffer_52_.caret = i_93_;
                RSBuffer_53_.caret = i_95_;
                RSBuffer_54_.caret = i_96_;
                RSBuffer_55_.caret = i_97_;
                RSBuffer_56_.caret = i_98_;
                for(int i_116_ = 0; this.texturedTriangles > i_116_; i_116_++) {
                    int i_117_ = this.aByteArray1388[i_116_] & 0xff;
                    if((i_117_ ^ 0xffffffff) == -1) {
                        this.aShortArray1403[i_116_] = (short) RSBuffer.readUnsignedShort();
                        this.aShortArray1421[i_116_] = (short) RSBuffer.readUnsignedShort();
                        this.aShortArray1385[i_116_] = (short) RSBuffer.readUnsignedShort();
                    }
                    if(i_117_ == 1) {
                        this.aShortArray1403[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        this.aShortArray1421[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        this.aShortArray1385[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        if(this.formatType < 15) {
                            this.anIntArray1389[i_116_] = RSBuffer_53_.readUnsignedShort();
                            if(this.formatType >= 14) {
                                this.anIntArray1404[i_116_] = RSBuffer_53_.method1186();
                            } else {
                                this.anIntArray1404[i_116_] = RSBuffer_53_.readUnsignedShort();
                            }
                            this.anIntArray1390[i_116_] = RSBuffer_53_.readUnsignedShort();
                        } else {
                            this.anIntArray1389[i_116_] = RSBuffer_53_.method1186();
                            this.anIntArray1404[i_116_] = RSBuffer_53_.method1186();
                            this.anIntArray1390[i_116_] = (RSBuffer_53_.method1186());
                        }
                        this.aByteArray1423[i_116_] = RSBuffer_54_.readSignedByte();
                        this.aByteArray1399[i_116_] = RSBuffer_55_.readSignedByte();
                        this.anIntArray1412[i_116_] = RSBuffer_56_.readSignedByte();
                    }
                    if(i_117_ == 2) {
                        this.aShortArray1403[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        this.aShortArray1421[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        this.aShortArray1385[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        if((this.formatType ^ 0xffffffff) <= -16) {
                            this.anIntArray1389[i_116_] = (RSBuffer_53_.method1186());
                            this.anIntArray1404[i_116_] = RSBuffer_53_.method1186();
                            this.anIntArray1390[i_116_] = RSBuffer_53_.method1186();
                        } else {
                            this.anIntArray1389[i_116_] = RSBuffer_53_.readUnsignedShort();
                            if(this.formatType < 14) {
                                this.anIntArray1404[i_116_] = RSBuffer_53_.readUnsignedShort();
                            } else {
                                this.anIntArray1404[i_116_] = RSBuffer_53_.method1186();
                            }
                            this.anIntArray1390[i_116_] = RSBuffer_53_.readUnsignedShort();
                        }
                        this.aByteArray1423[i_116_] = RSBuffer_54_.readSignedByte();
                        this.aByteArray1399[i_116_] = RSBuffer_55_.readSignedByte();
                        this.anIntArray1412[i_116_] = RSBuffer_56_.readSignedByte();
                        this.anIntArray1397[i_116_] = RSBuffer_56_.readSignedByte();
                        this.anIntArray1386[i_116_] = RSBuffer_56_.readSignedByte();
                    }
                    if(i_117_ == 3) {
                        this.aShortArray1403[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        this.aShortArray1421[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        this.aShortArray1385[i_116_] = (short) RSBuffer_52_.readUnsignedShort();
                        if(this.formatType < 15) {
                            this.anIntArray1389[i_116_] = RSBuffer_53_.readUnsignedShort();
                            if((this.formatType ^ 0xffffffff) > -15) {
                                this.anIntArray1404[i_116_] = RSBuffer_53_.readUnsignedShort();
                            } else {
                                this.anIntArray1404[i_116_] = RSBuffer_53_.method1186();
                            }
                            this.anIntArray1390[i_116_] = RSBuffer_53_.readUnsignedShort();
                        } else {
                            this.anIntArray1389[i_116_] = RSBuffer_53_.method1186();
                            this.anIntArray1404[i_116_] = RSBuffer_53_.method1186();
                            this.anIntArray1390[i_116_] = RSBuffer_53_.method1186();
                        }
                        this.aByteArray1423[i_116_] = RSBuffer_54_.readSignedByte();
                        this.aByteArray1399[i_116_] = RSBuffer_55_.readSignedByte();
                        this.anIntArray1412[i_116_] = RSBuffer_56_.readSignedByte();
                    }
                }
                RSBuffer.caret = i_99_;
                if(bool_59_) {
                    int i_118_ = RSBuffer.readUnsignedByte();
                    if((i_118_ ^ 0xffffffff) < -1) {
                        this.modelParticles = new ModelParticle[i_118_];
                        for(int i_119_ = 0; (i_118_ ^ 0xffffffff) < (i_119_ ^ 0xffffffff); i_119_++) {
                            int particleId = RSBuffer.readUnsignedShort();
                            if(particleId == toOverrideParticleId) {
                                particleId = customParticleId;
                            }
                            System.out.println(particleId + " - " + customParticleId + " - " + toOverrideParticleId);
                            int i_121_ = RSBuffer.readUnsignedShort();
                            byte i_122_;
                            if((i_62_ ^ 0xffffffff) != -256) {
                                i_122_ = (byte) i_62_;
                            } else {
                                i_122_ = this.aByteArray1402[i_121_];
                            }
                            this.modelParticles[i_119_] = new ModelParticle(particleId, (this.aShortArray1393[i_121_]),
                                (this.aShortArray1410[i_121_]), (this.aShortArray1392[i_121_]), i_122_);
                        }
                    }
                    int i_123_ = RSBuffer.readUnsignedByte();
                    if(i_123_ > 0) {
                        this.aClass35Array1398 = new Class35[i_123_];
                        for(int i_124_ = 0; (i_123_ ^ 0xffffffff) < (i_124_ ^ 0xffffffff); i_124_++) {
                            int i_125_ = RSBuffer.readUnsignedShort();
                            int i_126_ = RSBuffer.readUnsignedShort();
                            this.aClass35Array1398[i_124_] = new Class35(i_125_, i_126_);
                        }
                    }
                }
                if(!bool_60_) {
                    break;
                }
                
                int i_127_ = RSBuffer.readUnsignedByte();
                if(i_127_ <= 0) {
                    break;
                }
                this.aClass106Array1419 = new Class106[i_127_];
                for(int i_128_ = 0; i_128_ < i_127_; i_128_++) {
                    int i_129_ = RSBuffer.readUnsignedShort();
                    int i_130_ = RSBuffer.readUnsignedShort();
                    int i_131_ = RSBuffer.readUnsignedByte();
                    byte i_132_ = RSBuffer.readSignedByte();
                    this.aClass106Array1419[i_128_] = new Class106(i_129_, i_130_, i_131_, i_132_);
                }
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception,
                    ("lv.E(" + (is != null ? "{...}" : "null") + ',' + i + ')'));
            }
            break;
        } while(false);
    }
    
    private final void decoder_newest_format(byte[] paramArrayOfByte) {
        do {
            RSBuffer localOCI1 = new RSBuffer(paramArrayOfByte);
            RSBuffer localOCI2 = new RSBuffer(paramArrayOfByte);
            RSBuffer localOCI3 = new RSBuffer(paramArrayOfByte);
            RSBuffer localOCI4 = new RSBuffer(paramArrayOfByte);
            RSBuffer localOCI5 = new RSBuffer(paramArrayOfByte);
            RSBuffer localOCI6 = new RSBuffer(paramArrayOfByte);
            RSBuffer localOCI7 = new RSBuffer(paramArrayOfByte);
            if(newProtocol) {
                if(localOCI1.readUnsignedByte() != 1) {
                    throw new RuntimeException("Invalid Type!");
                }
                localOCI1.readUnsignedByte();
                formatType = localOCI1.readUnsignedByte();
                localOCI1.caret = paramArrayOfByte.length - 26;
            } else {
                localOCI1.caret = paramArrayOfByte.length - 23;
            }
            this.vertices = localOCI1.readUnsignedShort();
            this.triangles = localOCI1.readUnsignedShort();
            if(this.newProtocol) {
                this.texturedTriangles = localOCI1.readUnsignedShort();
            } else {
                this.texturedTriangles = localOCI1.readUnsignedByte();
            }
            int i = localOCI1.readUnsignedByte();
            boolean j = (i & 0x1) == 1;
            boolean k = (i & 0x2) == 2;
            boolean m = (i & 0x4) == 4;
            boolean n = (i & 0x8) == 8;
            int i1 = (i & 0x10) == 16 ? 1 : 0;
            int i2 = (i & 0x20) == 32 ? 1 : 0;
            int i3 = (i & 0x40) == 64 ? 1 : 0;
            int i4 = (i & 0x80) == 128 ? 1 : 0;
            if(n) {
                localOCI1.caret -= 7;
                this.formatType = localOCI1.readUnsignedByte();
                localOCI1.caret += 6;
            }
            int i5 = localOCI1.readUnsignedByte();
            int i6 = localOCI1.readUnsignedByte();
            int i7 = localOCI1.readUnsignedByte();
            int i8 = localOCI1.readUnsignedByte();
            int i9 = localOCI1.readUnsignedByte();
            int i10 = localOCI1.readUnsignedShort();
            int i11 = localOCI1.readUnsignedShort();
            int i12 = localOCI1.readUnsignedShort();
            int i13 = localOCI1.readUnsignedShort();
            int i14 = localOCI1.readUnsignedShort();
            int i15;
            int i16;
            if(this.newProtocol) {
                i15 = localOCI1.readUnsignedShort();
                i16 = localOCI1.readUnsignedShort();
                if(i1 == 0) {
                    if(i9 == 1) {
                        i15 = this.vertices;
                    } else {
                        i15 = 0;
                    }
                }
                if(i2 == 0) {
                    if(i7 == 1) {
                        i16 = this.triangles;
                    } else {
                        i16 = 0;
                    }
                }
            } else {
                i15 = 0;
                if(i1 != 0) {
                    i15 = localOCI1.readUnsignedShort();
                } else if(i9 == 1) {
                    i15 = this.vertices;
                }
                i16 = 0;
                if(i2 != 0) {
                    i16 = localOCI1.readUnsignedShort();
                } else if(i7 == 1) {
                    i16 = this.triangles;
                }
            }
            int i17 = 0;
            int i18 = 0;
            int i19 = 0;
            if(this.texturedTriangles > 0) {
                localOCI1.caret = (this.newProtocol ? 3 : 0);
                this.aByteArray1388 = new byte[this.texturedTriangles];
                for(int i_75_ = 0; i_75_ < this.texturedTriangles; i_75_++) {
                    byte i_76_ = (this.aByteArray1388[i_75_] = localOCI1.readSignedByte());
                    if((i_76_ ^ 0xffffffff) == -3) {
                        i19++;
                    }
                    if((i_76_ ^ 0xffffffff) == -1) {
                        i17++;
                    }
                    if((i_76_ ^ 0xffffffff) <= -2 && i_76_ <= 3) {
                        i18++;
                    }
                }
            }
            int i20 = this.texturedTriangles;
            if(this.newProtocol) {
                i20 += 3;
            }
            int i21 = i20;
            i20 += this.vertices;
            int i22 = i20;
            if(j) {
                i20 += this.triangles;
            }
            int i23 = i20;
            i20 += this.triangles;
            int i24 = i20;
            if(i5 == 255) {
                i20 += this.triangles;
            }
            int i25 = i20;
            i20 += i16;
            int i26 = i20;
            i20 += i15;
            int i27 = i20;
            if(i6 == 1) {
                i20 += this.triangles;
            }
            int i28 = i20;
            i20 += i13;
            int i29 = i20;
            if(i8 == 1) {
                i20 += 2 * this.triangles;
            }
            int i30 = i20;
            i20 += i14;
            int i31 = i20;
            i20 += 2 * this.triangles;
            int i32 = i20;
            i20 += i10;
            int i33 = i20;
            i20 += i11;
            int i34 = i20;
            i20 += i12;
            int i35 = i20;
            i20 += i17 * 6;
            int i36 = i20;
            i20 += i18 * 6;
            int i37 = 6;
            if((this.formatType ^ 0xffffffff) != -15) {
                if((this.formatType ^ 0xffffffff) <= -16) {
                    i37 = 9;
                }
            } else {
                i37 = 7;
            }
            int i38 = i20;
            i20 += i18 * i37;
            int i39 = i20;
            i20 += i18;
            int i40 = i20;
            i20 += i18;
            int i41 = i20;
            i20 += 2 * i19 + i18;
            int i42 = i20;
            int i43 = paramArrayOfByte.length;
            int i44 = paramArrayOfByte.length;
            int i45 = paramArrayOfByte.length;
            int i46 = paramArrayOfByte.length;
            if(i4 != 0) {
                RSBuffer localOCI8 = new RSBuffer(paramArrayOfByte);
                localOCI8.caret = (paramArrayOfByte.length - 26);
                localOCI8.caret -= paramArrayOfByte[(localOCI8.caret - 1)];
                this.anInt1413 = localOCI8.readUnsignedShort();
                int i48 = localOCI8.readUnsignedShort();
                int i49 = localOCI8.readUnsignedShort();
                i43 = i42 + i48;
                i44 = i43 + i49;
                i45 = i44 + this.vertices;
                i46 = i45 + this.anInt1413 * 2;
                // System.out.println("TODO!!");
            }
            this.aShortArray1410 = new short[this.triangles];
            this.aShortArray1393 = new short[this.triangles];
            int i_99_ = i20;
            if((i7 ^ 0xffffffff) == -2) {
                this.anIntArray1395 = new int[this.triangles];
            }
            if(j) {
                this.aByteArray1414 = new byte[this.triangles];
            }
            if((i6 ^ 0xffffffff) == -2) {
                this.aByteArray1411 = new byte[this.triangles];
            }
            if((this.texturedTriangles ^ 0xffffffff) < -1) {
                if((i18 ^ 0xffffffff) < -1) {
                    this.anIntArray1412 = new int[i18];
                    this.anIntArray1389 = new int[i18];
                    this.aByteArray1423 = new byte[i18];
                    this.aByteArray1399 = new byte[i18];
                    this.anIntArray1404 = new int[i18];
                    this.anIntArray1390 = new int[i18];
                }
                if((i19 ^ 0xffffffff) < -1) {
                    this.anIntArray1397 = new int[i19];
                    this.anIntArray1386 = new int[i19];
                }
                this.aShortArray1403 = new short[this.texturedTriangles];
                this.aShortArray1421 = new short[this.texturedTriangles];
                this.aShortArray1385 = new short[this.texturedTriangles];
            }
            this.anIntArray1407 = new int[this.vertices];
            if(i8 == 1) {
                this.aShortArray1409 = new short[this.triangles];
            }
            this.aShortArray1392 = new short[this.triangles];
            this.aShortArray1415 = new short[this.triangles];
            if((i5 ^ 0xffffffff) == -256) {
                this.aByteArray1402 = new byte[this.triangles];
            } else {
                this.aByte1422 = (byte) i5;
            }
            this.anIntArray1408 = new int[this.vertices];
            this.anIntArray1409 = new int[this.vertices];
            if(i8 == 1 && (this.texturedTriangles ^ 0xffffffff) < -1) {
                this.aByteArray1420 = new byte[this.triangles];
            }
            if((i9 ^ 0xffffffff) == -2) {
                this.anIntArray1411 = new int[this.vertices];
            }
            localOCI1.caret = i21;
            localOCI2.caret = i32;
            localOCI3.caret = i33;
            localOCI4.caret = i34;
            localOCI5.caret = i26;
            int i47 = 0;
            int i48 = 0;
            int i49 = 0;
            for(int i_103_ = 0; ((this.vertices ^ 0xffffffff) < (i_103_ ^ 0xffffffff)); i_103_++) {
                int i51 = localOCI1.readUnsignedByte();
                int i_105_ = 0;
                if((0x1 & i51 ^ 0xffffffff) != -1) {
                    i_105_ = localOCI2.method1239();
                }
                int i_106_ = 0;
                if((i51 & 0x2 ^ 0xffffffff) != -1) {
                    i_106_ = localOCI3.method1239();
                }
                int i_107_ = 0;
                if((0x4 & i51) != 0) {
                    i_107_ = localOCI4.method1239();
                }
                this.anIntArray1407[i_103_] = i47 + i_105_;
                this.anIntArray1408[i_103_] = i48 + i_106_;
                this.anIntArray1409[i_103_] = i49 + i_107_;
                i48 = this.anIntArray1408[i_103_];
                i49 = this.anIntArray1409[i_103_];
                i47 = this.anIntArray1407[i_103_];
                if(i9 == 1) {
                    if(i1 != 0) {
                        this.anIntArray1411[i_103_] = localOCI5.readSmart2();
                    } else {
                        int id = localOCI5.readUnsignedByte();
                        if(id == 255) {
                            id = -1;
                        }
                        this.anIntArray1411[i_103_] = id;
                    }
                }
            }
            if(anInt1413 > 0) {
                System.out.println("add shit here?");
            }
            localOCI1.caret = i31;
            localOCI2.caret = i22;
            localOCI3.caret = i24;
            localOCI4.caret = i27;
            localOCI5.caret = i25;
            localOCI6.caret = i29;
            localOCI7.caret = i30;
            for(int i_108_ = 0; this.triangles > i_108_; i_108_++) {
                this.aShortArray1415[i_108_] = (short) localOCI1.readUnsignedShort();
                if(j) {
                    this.aByteArray1414[i_108_] = localOCI2.readSignedByte();
                }
                if(i5 == 255) {
                    this.aByteArray1402[i_108_] = localOCI3.readSignedByte();
                }
                if((i6 ^ 0xffffffff) == -2) {
                    this.aByteArray1411[i_108_] = localOCI4.readSignedByte();
                }
                if(i7 == 1) {
                    if(i2 != 0) {
                        this.anIntArray1395[i_108_] = localOCI5.readSmart2();
                    } else {
                        int id = localOCI5.readUnsignedByte();
                        if(id == 255) {
                            id = -1;
                        }
                        this.anIntArray1395[i_108_] = id;
                    }
                }
                if(i8 == 1) {
                    this.aShortArray1409[i_108_] = (short) (localOCI6.readUnsignedShort() + -1);
                }
                if(this.aByteArray1420 != null) {
                    if(this.aShortArray1409[i_108_] == -1) {
                        this.aByteArray1420[i_108_] = (byte) -1;
                    } else if(this.formatType >= 16) {
                        this.aByteArray1420[i_108_] = (byte) (localOCI7.readSmart(454) - 1);// array
                        // should
                        // be
                        // short?
                    } else {
                        this.aByteArray1420[i_108_] = (byte) (localOCI7.readUnsignedByte() + -1);
                    }
                }
            }
            this.anInt1406 = -1;
            localOCI1.caret = i28;
            localOCI2.caret = i23;
            if(this.newProtocol) {
                localOCI3.caret = i43;
            }
            short i50 = 0;
            short i51 = 0;
            short i52 = 0;
            int i53 = 0;
            for(int i_113_ = 0; i_113_ < this.triangles; i_113_++) {
                int i55 = localOCI2.readUnsignedByte();
                int i56 = !this.newProtocol ? i55 : i55 & 0x7;
                if((i56 ^ 0xffffffff) == -2) {
                    i50 = (short) (i53 + localOCI1.method1239());
                    i53 = i50;
                    i51 = (short) (localOCI1.method1239() + i53);
                    i53 = i51;
                    i52 = (short) (localOCI1.method1239() + i53);
                    i53 = i52;
                    this.aShortArray1393[i_113_] = i50;
                    this.aShortArray1410[i_113_] = i51;
                    this.aShortArray1392[i_113_] = i52;
                    if(i50 > this.anInt1406) {
                        this.anInt1406 = i50;
                    }
                    if(this.anInt1406 < i51) {
                        this.anInt1406 = i51;
                    }
                    if(i52 > this.anInt1406) {
                        this.anInt1406 = i52;
                    }
                }
                if((i56 ^ 0xffffffff) == -3) {
                    i51 = i52;
                    i52 = (short) (i53 + localOCI1.method1239());
                    i53 = i52;
                    this.aShortArray1393[i_113_] = i50;
                    this.aShortArray1410[i_113_] = i51;
                    this.aShortArray1392[i_113_] = i52;
                    if((i52 ^ 0xffffffff) < (this.anInt1406 ^ 0xffffffff)) {
                        this.anInt1406 = i52;
                    }
                }
                if((i56 ^ 0xffffffff) == -4) {
                    i50 = i52;
                    i52 = (short) (i53 + localOCI1.method1239());
                    i53 = i52;
                    this.aShortArray1393[i_113_] = i50;
                    this.aShortArray1410[i_113_] = i51;
                    this.aShortArray1392[i_113_] = i52;
                    if((this.anInt1406 ^ 0xffffffff) > (i52 ^ 0xffffffff)) {
                        this.anInt1406 = i52;
                    }
                }
                if((i56 ^ 0xffffffff) == -5) {
                    short i57 = i50;
                    i50 = i51;
                    i51 = i57;
                    i52 = (short) (i53 + localOCI1.method1239());
                    i53 = i52;
                    this.aShortArray1393[i_113_] = i50;
                    this.aShortArray1410[i_113_] = i51;
                    this.aShortArray1392[i_113_] = i52;
                    if((this.anInt1406 ^ 0xffffffff) > (i52 ^ 0xffffffff)) {
                        this.anInt1406 = i52;
                    }
                }
                if((this.anInt1413 > 0) && ((i55 & 0x8) != 0)) {
                    localOCI3.readUnsignedByte();
                    localOCI3.readUnsignedByte();
                    localOCI3.readUnsignedByte();
                    // this.aByteArray1401[i54] = ((byte)
                    // localOCI3.readUnsignedByte(-92));
                    // this.aByteArray1421[i54] = ((byte)
                    // localOCI3.readUnsignedByte(-92));
                    // this.aByteArray1422[i54] = ((byte)
                    // localOCI3.readUnsignedByte(-92));
                }
            }
            this.anInt1406++;
            localOCI1.caret = i35;
            localOCI2.caret = i36;
            localOCI3.caret = i38;
            localOCI4.caret = i39;
            localOCI5.caret = i40;
            localOCI6.caret = i41;
            for(int i_116_ = 0; this.texturedTriangles > i_116_; i_116_++) {
                int i_117_ = this.aByteArray1388[i_116_] & 0xff;
                if((i_117_ ^ 0xffffffff) == -1) {
                    this.aShortArray1403[i_116_] = (short) localOCI1.readUnsignedShort();
                    this.aShortArray1421[i_116_] = (short) localOCI1.readUnsignedShort();
                    this.aShortArray1385[i_116_] = (short) localOCI1.readUnsignedShort();
                }
                if(i_117_ == 1) {
                    this.aShortArray1403[i_116_] = (short) localOCI2.readUnsignedShort();
                    this.aShortArray1421[i_116_] = (short) localOCI2.readUnsignedShort();
                    this.aShortArray1385[i_116_] = (short) localOCI2.readUnsignedShort();
                    if(this.formatType < 15) {
                        this.anIntArray1389[i_116_] = localOCI3.readUnsignedShort();
                        if(this.formatType >= 14) {
                            this.anIntArray1404[i_116_] = localOCI3.method1186();
                        } else {
                            this.anIntArray1404[i_116_] = localOCI3.readUnsignedShort();
                        }
                        this.anIntArray1390[i_116_] = localOCI3.readUnsignedShort();
                    } else {
                        this.anIntArray1389[i_116_] = localOCI3.method1186();
                        this.anIntArray1404[i_116_] = localOCI3.method1186();
                        this.anIntArray1390[i_116_] = (localOCI3.method1186());
                    }
                    this.aByteArray1423[i_116_] = localOCI4.readSignedByte();
                    this.aByteArray1399[i_116_] = localOCI5.readSignedByte();
                    this.anIntArray1412[i_116_] = localOCI6.readSignedByte();
                }
                if(i_117_ == 2) {
                    this.aShortArray1403[i_116_] = (short) localOCI2.readUnsignedShort();
                    this.aShortArray1421[i_116_] = (short) localOCI2.readUnsignedShort();
                    this.aShortArray1385[i_116_] = (short) localOCI2.readUnsignedShort();
                    if((this.formatType ^ 0xffffffff) <= -16) {
                        this.anIntArray1389[i_116_] = (localOCI3.method1186());
                        this.anIntArray1404[i_116_] = localOCI3.method1186();
                        this.anIntArray1390[i_116_] = localOCI3.method1186();
                    } else {
                        this.anIntArray1389[i_116_] = localOCI3.readUnsignedShort();
                        if(this.formatType < 14) {
                            this.anIntArray1404[i_116_] = localOCI3.readUnsignedShort();
                        } else {
                            this.anIntArray1404[i_116_] = localOCI3.method1186();
                        }
                        this.anIntArray1390[i_116_] = localOCI3.readUnsignedShort();
                    }
                    this.aByteArray1423[i_116_] = localOCI4.readSignedByte();
                    this.aByteArray1399[i_116_] = localOCI5.readSignedByte();
                    this.anIntArray1412[i_116_] = localOCI6.readSignedByte();
                    this.anIntArray1397[i_116_] = localOCI6.readSignedByte();
                    this.anIntArray1386[i_116_] = localOCI6.readSignedByte();
                }
                if(i_117_ == 3) {
                    this.aShortArray1403[i_116_] = (short) localOCI2.readUnsignedShort();
                    this.aShortArray1421[i_116_] = (short) localOCI2.readUnsignedShort();
                    this.aShortArray1385[i_116_] = (short) localOCI2.readUnsignedShort();
                    if(this.formatType < 15) {
                        this.anIntArray1389[i_116_] = localOCI3.readUnsignedShort();
                        if((this.formatType ^ 0xffffffff) > -15) {
                            this.anIntArray1404[i_116_] = localOCI3.readUnsignedShort();
                        } else {
                            this.anIntArray1404[i_116_] = localOCI3.method1186();
                        }
                        this.anIntArray1390[i_116_] = localOCI3.readUnsignedShort();
                    } else {
                        this.anIntArray1389[i_116_] = localOCI3.method1186();
                        this.anIntArray1404[i_116_] = localOCI3.method1186();
                        this.anIntArray1390[i_116_] = localOCI3.method1186();
                    }
                    this.aByteArray1423[i_116_] = localOCI4.readSignedByte();
                    this.aByteArray1399[i_116_] = localOCI5.readSignedByte();
                    this.anIntArray1412[i_116_] = localOCI6.readSignedByte();
                }
            }
            localOCI1.caret = i_99_;
            if(k) {
                int i_118_ = localOCI1.readUnsignedByte();
                if((i_118_ ^ 0xffffffff) < -1) {
                    this.modelParticles = new ModelParticle[i_118_];
                    for(int i_119_ = 0; (i_118_ ^ 0xffffffff) < (i_119_ ^ 0xffffffff); i_119_++) {
                        int particleId = localOCI1.readUnsignedShort();
                        if(particleId == toOverrideParticleId) {
                            particleId = customParticleId;
                        }
                        System.out.println("Particle id = " + particleId + " - " + customParticleId + " - "
                            + toOverrideParticleId);
                        int i_121_ = localOCI1.readUnsignedShort();
                        byte i_122_;
                        if((i5 ^ 0xffffffff) != -256) {
                            i_122_ = (byte) i5;
                        } else {
                            i_122_ = this.aByteArray1402[i_121_];
                        }
                        this.modelParticles[i_119_] = new ModelParticle(particleId, (this.aShortArray1393[i_121_]),
                            (this.aShortArray1410[i_121_]), (this.aShortArray1392[i_121_]), i_122_);
                    }
                }
                int i_123_ = localOCI1.readUnsignedByte();
                if(i_123_ > 0) {
                    this.aClass35Array1398 = new Class35[i_123_];
                    for(int i_124_ = 0; (i_123_ ^ 0xffffffff) < (i_124_ ^ 0xffffffff); i_124_++) {
                        int i_125_ = localOCI1.readUnsignedShort();
                        int i_126_ = localOCI1.readUnsignedShort();
                        this.aClass35Array1398[i_124_] = new Class35(i_125_, i_126_);
                    }
                }
            }
            if(!m) {
                break;
            }
            
            int i_127_ = localOCI1.readUnsignedByte();
            if(i_127_ <= 0) {
                break;
            }
            this.aClass106Array1419 = new Class106[i_127_];
            for(int i_128_ = 0; i_128_ < i_127_; i_128_++) {
                int i_129_ = localOCI1.readUnsignedShort();
                int i_130_ = localOCI1.readUnsignedShort();
                int i_131_ = 0;
                if(i3 != 0) {
                    i_131_ = localOCI1.readSmart2();
                } else {
                    i_131_ = localOCI1.readUnsignedByte();
                    if(i_131_ == 255) {
                        i_131_ = -1;
                    }
                }
                byte i_132_ = localOCI1.readSignedByte();
                this.aClass106Array1419[i_128_] = new Class106(i_129_, i_130_, i_131_, i_132_);
            }
            break;
        } while(false);
    }
    
    void dumpColours() {
        
        if(true) {
            return;
        }
        
        List<Short> tmp = new ArrayList<Short>();
        if(this.aShortArray1415 != null) {
            for(Short sh : aShortArray1415) {
                if(!tmp.contains(sh)) {
                    System.err.println("color: " + sh);
                    tmp.add(sh);
                }
            }
        }
    }
    
    private final void method2587(byte[] is, int i) {
        
        do {
            try {
                boolean bool = false;
                boolean bool_0_ = false;
                RSBuffer RSBuffer = new RSBuffer(is);
                RSBuffer RSBuffer_1_ = new RSBuffer(is);
                RSBuffer RSBuffer_2_ = new RSBuffer(is);
                RSBuffer RSBuffer_3_ = new RSBuffer(is);
                RSBuffer RSBuffer_4_ = new RSBuffer(is);
                RSBuffer.caret = -18 + is.length;
                this.vertices = RSBuffer.readUnsignedShort();
                this.triangles = RSBuffer.readUnsignedShort();
                this.texturedTriangles = RSBuffer.readUnsignedByte();
                int i_5_ = RSBuffer.readUnsignedByte();
                int i_6_ = RSBuffer.readUnsignedByte();
                int i_7_ = RSBuffer.readUnsignedByte();
                int i_8_ = RSBuffer.readUnsignedByte();
                int i_9_ = RSBuffer.readUnsignedByte();
                int i_10_ = RSBuffer.readUnsignedShort();
                int i_11_ = RSBuffer.readUnsignedShort();
                int i_12_ = RSBuffer.readUnsignedShort();
                int i_13_ = RSBuffer.readUnsignedShort();
                int i_14_ = 0;
                int i_15_ = i_14_;
                i_14_ += this.vertices;
                int i_16_ = i_14_;
                i_14_ += this.triangles;
                int i_17_ = i_14_;
                if(i_6_ == 255) {
                    i_14_ += this.triangles;
                }
                int i_18_ = i_14_;
                if((i_8_ ^ 0xffffffff) == -2) {
                    i_14_ += this.triangles;
                }
                int i_19_ = i_14_;
                if(i_5_ == 1) {
                    i_14_ += this.triangles;
                }
                int i_20_ = i_14_;
                if((i_9_ ^ 0xffffffff) == -2) {
                    i_14_ += this.vertices;
                }
                int i_21_ = i_14_;
                if(i_7_ == 1) {
                    i_14_ += this.triangles;
                }
                int i_22_ = i_14_;
                i_14_ += i_13_;
                int i_23_ = i_14_;
                i_14_ += 2 * this.triangles;
                int i_24_ = i_14_;
                i_14_ += this.texturedTriangles * 6;
                int i_25_ = i_14_;
                i_14_ += i_10_;
                int i_26_ = i_14_;
                i_14_ += i_11_;
                int i_27_ = i_14_;
                this.anIntArray1407 = new int[this.vertices];
                if(i_7_ == 1) {
                    this.aByteArray1411 = new byte[this.triangles];
                }
                if(this.texturedTriangles > 0) {
                    this.aShortArray1421 = new short[this.texturedTriangles];
                    this.aShortArray1385 = new short[this.texturedTriangles];
                    this.aShortArray1403 = new short[this.texturedTriangles];
                    this.aByteArray1388 = new byte[this.texturedTriangles];
                }
                if(i_5_ == 1) {
                    this.aByteArray1414 = new byte[this.triangles];
                    this.aByteArray1420 = new byte[this.triangles];
                    this.aShortArray1409 = new short[this.triangles];
                }
                this.aShortArray1415 = new short[this.triangles];
                this.anIntArray1409 = new int[this.vertices];
                this.anIntArray1408 = new int[this.vertices];
                this.aShortArray1410 = new short[this.triangles];
                i_14_ += i_12_;
                this.aShortArray1392 = new short[this.triangles];
                RSBuffer.caret = i_15_;
                this.aShortArray1393 = new short[this.triangles];
                if((i_8_ ^ 0xffffffff) == -2) {
                    this.anIntArray1395 = new int[this.triangles];
                }
                if((i_6_ ^ 0xffffffff) == -256) {
                    this.aByteArray1402 = new byte[this.triangles];
                } else {
                    this.aByte1422 = (byte) i_6_;
                }
                if(i_9_ == 1) {
                    this.anIntArray1411 = new int[this.vertices];
                }
                RSBuffer_1_.caret = i_25_;
                RSBuffer_2_.caret = i_26_;
                RSBuffer_3_.caret = i_27_;
                RSBuffer_4_.caret = i_20_;
                int i_28_ = 0;
                int i_29_ = 0;
                int i_30_ = 0;
                for(int i_31_ = 0; this.vertices > i_31_; i_31_++) {
                    int i_32_ = RSBuffer.readUnsignedByte();
                    int i_33_ = 0;
                    if((i_32_ & 0x1 ^ 0xffffffff) != -1) {
                        i_33_ = RSBuffer_1_.method1239();
                    }
                    int i_34_ = 0;
                    if((i_32_ & 0x2 ^ 0xffffffff) != -1) {
                        i_34_ = RSBuffer_2_.method1239();
                    }
                    int i_35_ = 0;
                    if((0x4 & i_32_ ^ 0xffffffff) != -1) {
                        i_35_ = RSBuffer_3_.method1239();
                    }
                    this.anIntArray1407[i_31_] = i_33_ + i_28_;
                    this.anIntArray1408[i_31_] = i_34_ + i_29_;
                    this.anIntArray1409[i_31_] = i_30_ + i_35_;
                    i_29_ = this.anIntArray1408[i_31_];
                    i_30_ = this.anIntArray1409[i_31_];
                    i_28_ = this.anIntArray1407[i_31_];
                    if((i_9_ ^ 0xffffffff) == -2) {
                        this.anIntArray1411[i_31_] = RSBuffer_4_.readUnsignedByte();
                    }
                }
                RSBuffer.caret = i_23_;
                RSBuffer_1_.caret = i_19_;
                RSBuffer_2_.caret = i_17_;
                RSBuffer_3_.caret = i_21_;
                RSBuffer_4_.caret = i_18_;
                for(int i_36_ = 0; i_36_ < this.triangles; i_36_++) {
                    this.aShortArray1415[i_36_] = (short) RSBuffer.readUnsignedShort();
                    if((i_5_ ^ 0xffffffff) == -2) {
                        int i_37_ = RSBuffer_1_.readUnsignedByte();
                        if((i_37_ & 0x1 ^ 0xffffffff) != -2) {
                            this.aByteArray1414[i_36_] = (byte) 0;
                        } else {
                            bool = true;
                            this.aByteArray1414[i_36_] = (byte) 1;
                        }
                        if((0x2 & i_37_) != 2) {
                            this.aByteArray1420[i_36_] = (byte) -1;
                            this.aShortArray1409[i_36_] = (short) -1;
                        } else {
                            this.aByteArray1420[i_36_] = (byte) (i_37_ >> 2);
                            this.aShortArray1409[i_36_] = this.aShortArray1415[i_36_];
                            this.aShortArray1415[i_36_] = (short) 127;
                            if(this.aShortArray1409[i_36_] != -1) {
                                bool_0_ = true;
                            }
                        }
                    }
                    if((i_6_ ^ 0xffffffff) == -256) {
                        this.aByteArray1402[i_36_] = RSBuffer_2_.readSignedByte();
                    }
                    if(i_7_ == 1) {
                        this.aByteArray1411[i_36_] = RSBuffer_3_.readSignedByte();
                    }
                    if((i_8_ ^ 0xffffffff) == -2) {
                        this.anIntArray1395[i_36_] = RSBuffer_4_.readUnsignedByte();
                    }
                }
                this.anInt1406 = i;
                RSBuffer.caret = i_22_;
                RSBuffer_1_.caret = i_16_;
                short i_38_ = 0;
                short i_39_ = 0;
                short i_40_ = 0;
                int i_41_ = 0;
                for(int i_42_ = 0; this.triangles > i_42_; i_42_++) {
                    int i_43_ = RSBuffer_1_.readUnsignedByte();
                    if((i_43_ ^ 0xffffffff) == -2) {
                        i_38_ = (short) (RSBuffer.method1239() + i_41_);
                        i_41_ = i_38_;
                        i_39_ = (short) (RSBuffer.method1239() + i_41_);
                        i_41_ = i_39_;
                        i_40_ = (short) (RSBuffer.method1239() + i_41_);
                        this.aShortArray1393[i_42_] = i_38_;
                        i_41_ = i_40_;
                        this.aShortArray1410[i_42_] = i_39_;
                        this.aShortArray1392[i_42_] = i_40_;
                        if((this.anInt1406 ^ 0xffffffff) > (i_38_ ^ 0xffffffff)) {
                            this.anInt1406 = i_38_;
                        }
                        if(i_39_ > this.anInt1406) {
                            this.anInt1406 = i_39_;
                        }
                        if((this.anInt1406 ^ 0xffffffff) > (i_40_ ^ 0xffffffff)) {
                            this.anInt1406 = i_40_;
                        }
                    }
                    if(i_43_ == 2) {
                        i_39_ = i_40_;
                        i_40_ = (short) (i_41_ + RSBuffer.method1239());
                        this.aShortArray1393[i_42_] = i_38_;
                        i_41_ = i_40_;
                        this.aShortArray1410[i_42_] = i_39_;
                        this.aShortArray1392[i_42_] = i_40_;
                        if(i_40_ > this.anInt1406) {
                            this.anInt1406 = i_40_;
                        }
                    }
                    if((i_43_ ^ 0xffffffff) == -4) {
                        i_38_ = i_40_;
                        i_40_ = (short) (RSBuffer.method1239() + i_41_);
                        this.aShortArray1393[i_42_] = i_38_;
                        i_41_ = i_40_;
                        this.aShortArray1410[i_42_] = i_39_;
                        this.aShortArray1392[i_42_] = i_40_;
                        if(i_40_ > this.anInt1406) {
                            this.anInt1406 = i_40_;
                        }
                    }
                    if(i_43_ == 4) {
                        short i_44_ = i_38_;
                        i_38_ = i_39_;
                        i_39_ = i_44_;
                        i_40_ = (short) (RSBuffer.method1239() + i_41_);
                        this.aShortArray1393[i_42_] = i_38_;
                        i_41_ = i_40_;
                        this.aShortArray1410[i_42_] = i_39_;
                        this.aShortArray1392[i_42_] = i_40_;
                        if(this.anInt1406 < i_40_) {
                            this.anInt1406 = i_40_;
                        }
                    }
                }
                RSBuffer.caret = i_24_;
                this.anInt1406++;
                for(int i_45_ = 0; this.texturedTriangles > i_45_; i_45_++) {
                    this.aByteArray1388[i_45_] = (byte) 0;
                    this.aShortArray1403[i_45_] = (short) RSBuffer.readUnsignedShort();
                    this.aShortArray1421[i_45_] = (short) RSBuffer.readUnsignedShort();
                    this.aShortArray1385[i_45_] = (short) RSBuffer.readUnsignedShort();
                }
                if(this.aByteArray1420 != null) {
                    boolean bool_46_ = false;
                    for(int i_47_ = 0; ((i_47_ ^ 0xffffffff) > (this.triangles ^ 0xffffffff)); i_47_++) {
                        int i_48_ = 0xff & this.aByteArray1420[i_47_];
                        if((i_48_ ^ 0xffffffff) != -256) {
                            if(((0xffff & this.aShortArray1403[i_48_]) != this.aShortArray1393[i_47_])
                                || ((0xffff & (this.aShortArray1421[i_48_])) != (this.aShortArray1410[i_47_]))
                                || ((this.aShortArray1392[i_47_]
                                ^ 0xffffffff) != ((this.aShortArray1385[i_48_]) & 0xffff ^ 0xffffffff))) {
                                bool_46_ = true;
                            } else {
                                this.aByteArray1420[i_47_] = (byte) -1;
                            }
                        }
                    }
                    if(!bool_46_) {
                        this.aByteArray1420 = null;
                    }
                }
                if(!bool) {
                    this.aByteArray1414 = null;
                }
                if(bool_0_) {
                    break;
                }
                this.aShortArray1409 = null;
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception,
                    ("lv.H(" + (is != null ? "{...}" : "null") + ',' + i + ')'));
            }
            break;
        } while(false);
    }
    
    final void method2590(short i, byte i_133_, short i_134_) {
        
        try {
            int i_135_ = -15 / ((5 - i_133_) / 54);
            if(this.aShortArray1409 != null) {
                for(int i_136_ = 0; i_136_ < this.triangles; i_136_++) {
                    if((i_134_ ^ 0xffffffff) == (this.aShortArray1409[i_136_] ^ 0xffffffff)) {
                        this.aShortArray1409[i_136_] = i;
                    }
                }
            }
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("lv.C(" + i + ',' + i_133_ + ',' + i_134_ + ')'));
        }
    }
    
    final int[][] method2591(byte i) {
        
        try {
            int[] is = new int[256];
            int i_137_ = 0;
            for(int i_138_ = 0; ((i_138_ ^ 0xffffffff) > (this.triangles ^ 0xffffffff)); i_138_++) {
                int i_139_ = this.anIntArray1395[i_138_];
                if((i_139_ ^ 0xffffffff) <= -1) {
                    if((i_139_ ^ 0xffffffff) < (i_137_ ^ 0xffffffff)) {
                        i_137_ = i_139_;
                    }
                    is[i_139_]++;
                }
            }
            int[][] is_140_ = new int[1 + i_137_][];
            if(i <= 96) {
                return null;
            }
            for(int i_141_ = 0; i_137_ >= i_141_; i_141_++) {
                is_140_[i_141_] = new int[is[i_141_]];
                is[i_141_] = 0;
            }
            for(int i_142_ = 0; ((i_142_ ^ 0xffffffff) > (this.triangles ^ 0xffffffff)); i_142_++) {
                int i_143_ = this.anIntArray1395[i_142_];
                if((i_143_ ^ 0xffffffff) <= -1) {
                    is_140_[i_143_][is[i_143_]++] = i_142_;
                }
            }
            return is_140_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "lv.J(" + i + ')');
        }
    }
    
    final void method2592(int i, int i_144_) {
        
        do {
            try {
                if(i != 13746) {
                    method2596(-114);
                }
                for(int i_145_ = 0; this.vertices > i_145_; i_145_++) {
                    this.anIntArray1407[i_145_] <<= i_144_;
                    this.anIntArray1408[i_145_] <<= i_144_;
                    this.anIntArray1409[i_145_] <<= i_144_;
                }
                if((this.texturedTriangles ^ 0xffffffff) >= -1 || this.anIntArray1389 == null) {
                    break;
                }
                for(int i_146_ = 0; ((this.anIntArray1389.length ^ 0xffffffff) < (i_146_ ^ 0xffffffff)); i_146_++) {
                    this.anIntArray1389[i_146_] <<= i_144_;
                    this.anIntArray1404[i_146_] <<= i_144_;
                    if(this.aByteArray1388[i_146_] != 1) {
                        this.anIntArray1390[i_146_] <<= i_144_;
                    }
                }
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception, ("lv.L(" + i + ',' + i_144_ + ')'));
            }
            break;
        } while(false);
    }
    
    final int method2594(byte i, short i_150_, short i_151_, byte i_152_, boolean bool, int i_153_, int i_154_,
                         byte i_155_, int i_156_) {
        
        try {
            this.aShortArray1393[this.triangles] = (short) i_154_;
            this.aShortArray1410[this.triangles] = (short) i_156_;
            this.aShortArray1392[this.triangles] = (short) i_153_;
            this.aByteArray1414[this.triangles] = i;
            this.aByteArray1420[this.triangles] = i_155_;
            this.aShortArray1415[this.triangles] = i_150_;
            this.aByteArray1411[this.triangles] = i_152_;
            if(bool != false) {
                this.anIntArray1390 = null;
            }
            this.aShortArray1409[this.triangles] = i_151_;
            return this.triangles++;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("lv.F(" + i + ',' + i_150_ + ',' + i_151_ + ',' + i_152_
                + ',' + bool + ',' + i_153_ + ',' + i_154_ + ',' + i_155_ + ',' + i_156_ + ')'));
        }
    }
    
    final int[][] method2595(int i, boolean bool) {
        
        try {
            if(i < 17) {
                method2594((byte) -59, (short) -115, (short) -111, (byte) -111, true, -58, 126, (byte) -114, -57);
            }
            int[] is = new int[256];
            int i_157_ = 0;
            int i_158_ = (!bool ? this.anInt1406 : this.vertices);
            for(int i_159_ = 0; (i_158_ ^ 0xffffffff) < (i_159_ ^ 0xffffffff); i_159_++) {
                int i_160_ = this.anIntArray1411[i_159_];
                if(i_160_ >= 0) {
                    if((i_157_ ^ 0xffffffff) > (i_160_ ^ 0xffffffff)) {
                        i_157_ = i_160_;
                    }
                    is[i_160_]++;
                }
            }
            int[][] is_161_ = new int[i_157_ + 1][];
            for(int i_162_ = 0; i_157_ >= i_162_; i_162_++) {
                is_161_[i_162_] = new int[is[i_162_]];
                is[i_162_] = 0;
            }
            for(int i_163_ = 0; i_158_ > i_163_; i_163_++) {
                int i_164_ = this.anIntArray1411[i_163_];
                if((i_164_ ^ 0xffffffff) <= -1) {
                    is_161_[i_164_][is[i_164_]++] = i_163_;
                }
            }
            return is_161_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "lv.B(" + i + ',' + bool + ')');
        }
    }
    
    final int[][] method2596(int i) {
        
        try {
            int[] is = new int[256];
            int i_165_ = 0;
            for(int i_166_ = 0; ((this.aClass106Array1419.length ^ 0xffffffff) < (i_166_ ^ 0xffffffff)); i_166_++) {
                int i_167_ = (this.aClass106Array1419[i_166_].anInt908);
                if((i_167_ ^ 0xffffffff) <= -1) {
                    if(i_165_ < i_167_) {
                        i_165_ = i_167_;
                    }
                    is[i_167_]++;
                }
            }
            int[][] is_168_ = new int[i_165_ + 1][];
            for(int i_169_ = 0; (i_169_ ^ 0xffffffff) >= (i_165_ ^ 0xffffffff); i_169_++) {
                is_168_[i_169_] = new int[is[i_169_]];
                is[i_169_] = 0;
            }
            int i_170_ = 0;
            if(i != 21517) {
                this.anIntArray1409 = null;
            }
            for(/**/; ((i_170_ ^ 0xffffffff) > (this.aClass106Array1419.length ^ 0xffffffff)); i_170_++) {
                int i_171_ = (this.aClass106Array1419[i_170_].anInt908);
                if(i_171_ >= 0) {
                    is_168_[i_171_][is[i_171_]++] = i_170_;
                }
            }
            return is_168_;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, "lv.G(" + i + ')');
        }
    }
    
    final void method2597(int i, int i_172_, byte i_173_, int i_174_) {
        
        do {
            try {
                for(int i_175_ = 0; i_175_ < this.vertices; i_175_++) {
                    this.anIntArray1407[i_175_] += i_172_;
                    this.anIntArray1408[i_175_] += i_174_;
                    this.anIntArray1409[i_175_] += i;
                }
                if(i_173_ >= 54) {
                    break;
                }
                method2595(105, true);
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception,
                    ("lv.O(" + i + ',' + i_172_ + ',' + i_173_ + ',' + i_174_ + ')'));
            }
            break;
        } while(false);
    }
    
    private final int method2598(Model model_176_, int i, short i_177_, int i_178_) {
        
        try {
            int i_179_ = model_176_.anIntArray1407[i];
            int i_180_ = model_176_.anIntArray1408[i];
            int i_181_ = model_176_.anIntArray1409[i];
            for(int i_182_ = i_178_; ((this.vertices ^ 0xffffffff) < (i_182_ ^ 0xffffffff)); i_182_++) {
                if(this.anIntArray1407[i_182_] == i_179_ && this.anIntArray1408[i_182_] == i_180_
                    && this.anIntArray1409[i_182_] == i_181_) {
                    this.aShortArray1408[i_182_] = (short) Class41.method366((this.aShortArray1408[i_182_]), i_177_);
                    return i_182_;
                }
            }
            this.anIntArray1407[this.vertices] = i_179_;
            this.anIntArray1408[this.vertices] = i_180_;
            this.anIntArray1409[this.vertices] = i_181_;
            this.aShortArray1408[this.vertices] = i_177_;
            this.anIntArray1411[this.vertices] = (model_176_.anIntArray1411 != null ? model_176_.anIntArray1411[i]
                : -1);
            return this.vertices++;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("lv.M(" + (model_176_ != null ? "{...}" : "null") + ',' + i + ',' + i_177_ + ',' + i_178_ + ')'));
        }
    }
    
    final int method2599(int i, int i_183_, int i_184_, int i_185_) {
        
        try {
            for(int i_186_ = 0; this.vertices > i_186_; i_186_++) {
                if(((i_183_ ^ 0xffffffff) == (this.anIntArray1407[i_186_] ^ 0xffffffff))
                    && ((this.anIntArray1408[i_186_] ^ 0xffffffff) == (i_184_ ^ 0xffffffff))
                    && i_185_ == this.anIntArray1409[i_186_]) {
                    return i_186_;
                }
            }
            this.anIntArray1407[this.vertices] = i_183_;
            this.anIntArray1408[this.vertices] = i_184_;
            this.anIntArray1409[this.vertices] = i_185_;
            this.anInt1406 = this.vertices + 1;
            if(i != 14418) {
                method2598(null, -77, (short) 58, 51);
            }
            return this.vertices++;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception,
                ("lv.N(" + i + ',' + i_183_ + ',' + i_184_ + ',' + i_185_ + ')'));
        }
    }
    
    final void method2600(int i, int i_187_, byte i_188_, int i_189_) {
        
        do {
            try {
                if((i ^ 0xffffffff) != -1) {
                    int i_190_ = Class284_Sub2_Sub2.anIntArray6200[i];
                    int i_191_ = Class284_Sub2_Sub2.anIntArray6202[i];
                    for(int i_192_ = 0; i_192_ < this.vertices; i_192_++) {
                        int i_193_ = (((i_191_ * this.anIntArray1407[i_192_])
                            + (this.anIntArray1408[i_192_] * i_190_)) >> 14);
                        this.anIntArray1408[i_192_] = (-(this.anIntArray1407[i_192_] * i_190_)
                            + (this.anIntArray1408[i_192_] * i_191_)) >> 14;
                        this.anIntArray1407[i_192_] = i_193_;
                    }
                }
                int i_194_ = -79 / ((i_188_ - 49) / 63);
                if((i_187_ ^ 0xffffffff) != -1) {
                    int i_195_ = Class284_Sub2_Sub2.anIntArray6200[i_187_];
                    int i_196_ = Class284_Sub2_Sub2.anIntArray6202[i_187_];
                    for(int i_197_ = 0; this.vertices > i_197_; i_197_++) {
                        int i_198_ = (((this.anIntArray1408[i_197_] * i_196_)
                            + -(i_195_ * (this.anIntArray1409[i_197_]))) >> 14);
                        this.anIntArray1409[i_197_] = ((this.anIntArray1408[i_197_] * i_195_)
                            + (this.anIntArray1409[i_197_] * i_196_)) >> 14;
                        this.anIntArray1408[i_197_] = i_198_;
                    }
                }
                if(i_189_ == 0) {
                    break;
                }
                int i_199_ = Class284_Sub2_Sub2.anIntArray6200[i_189_];
                int i_200_ = Class284_Sub2_Sub2.anIntArray6202[i_189_];
                for(int i_201_ = 0; ((i_201_ ^ 0xffffffff) > (this.vertices ^ 0xffffffff)); i_201_++) {
                    int i_202_ = ((i_199_ * this.anIntArray1409[i_201_]
                        + (i_200_ * this.anIntArray1407[i_201_])) >> 14);
                    this.anIntArray1409[i_201_] = (this.anIntArray1409[i_201_] * i_200_
                        - i_199_ * (this.anIntArray1407[i_201_])) >> 14;
                    this.anIntArray1407[i_201_] = i_202_;
                }
            } catch(RuntimeException runtimeexception) {
                throw Class64_Sub27.method667(runtimeexception,
                    ("lv.I(" + i + ',' + i_187_ + ',' + i_188_ + ',' + i_189_ + ')'));
            }
            break;
        } while(false);
    }
    
    final byte method2601(byte i, byte i_203_, short i_204_, short i_205_, short i_206_, short i_207_, short i_208_,
                          byte i_209_, short i_210_, byte i_211_) {
        
        try {
            if(this.texturedTriangles >= 255) {
                throw new IllegalStateException();
            }
            this.aByteArray1388[this.texturedTriangles] = (byte) 3;
            this.aShortArray1403[this.texturedTriangles] = i_205_;
            this.aShortArray1421[this.texturedTriangles] = i_210_;
            this.aShortArray1385[this.texturedTriangles] = i_204_;
            this.anIntArray1389[this.texturedTriangles] = i_208_;
            this.anIntArray1404[this.texturedTriangles] = i_207_;
            this.anIntArray1390[this.texturedTriangles] = i_206_;
            this.aByteArray1423[this.texturedTriangles] = i_211_;
            this.aByteArray1399[this.texturedTriangles] = i;
            this.anIntArray1412[this.texturedTriangles] = i_203_;
            if(i_209_ <= 116) {
                return (byte) -112;
            }
            return (byte) this.texturedTriangles++;
        } catch(RuntimeException runtimeexception) {
            throw Class64_Sub27.method667(runtimeexception, ("lv.K(" + i + ',' + i_203_ + ',' + i_204_ + ',' + i_205_
                + ',' + i_206_ + ',' + i_207_ + ',' + i_208_ + ',' + i_209_ + ',' + i_210_ + ',' + i_211_ + ')'));
        }
    }
    
    void paintBlack() {
        
        if(this.aShortArray1415 != null) {
            this.aShortArray1415 = new short[this.aShortArray1415.length];
        }
    }
    
    final void recolor(int i, short i_147_, short i_148_) {
        for(int i_149_ = i; this.triangles > i_149_; i_149_++) {
            if((i_147_ ^ 0xffffffff) == (this.aShortArray1415[i_149_] ^ 0xffffffff)) {
                this.aShortArray1415[i_149_] = i_148_;
            }
        }
    }
    
    final void recolor2(short orig, short modifier) {
        for(int k = 0; k < aShortArray1415.length; k++) {
            if(aShortArray1415[k] == orig) {
                aShortArray1415[k] = modifier;
            }
        }
    }
}
