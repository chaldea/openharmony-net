// libcrypto_stub.c — Minimal OpenSSL-compatible libcrypto for HarmonyOS.
//
// .NET NativeAOT's System.Security.Cryptography.Native shim dlopens libssl.so
// (which transitively loads libcrypto.so), then dlsyms ~454 REQUIRED_FUNCTION
// symbols. If any symbol is missing it aborts. It then calls the EVP digest
// family to compute hashes (SHA-256 for pipeline/shader cache keys in XEngine).
//
// This file provides:
//   - A real, dependency-free SHA-256 implementation (FIPS 180-4).
//   - Working EVP_MD_CTX / EVP_Digest* / EVP_sha* wrappers around it.
//   - Working HMAC wrappers.
//   - Working no-op stubs for OPENSSL_init_ssl, RAND_bytes, ERR_*, BIO_*, CRYPTO_*,
//     OPENSSL_sk_* (return benign values so dlsym resolves and the shim proceeds).
//   - No-op stubs for every other REQUIRED_FUNCTION symbol (X509/SSL/RSA/AES/DSA/EC/...)
//     so dlsym resolves them. XEngine's HarmonyOS player never invokes these; they
//     exist only to keep the shim from aborting on a missing symbol.
//
// Compiled with the OHOS NDK clang for aarch64-linux-ohos into libcrypto.so.
// A companion libssl_stub.c builds libssl.so re-exporting the SSL_* symbols and
// linking against libcrypto.so.

#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>

// ===========================================================================
// SHA-256 (FIPS 180-4). Public-domain reference implementation.
// ===========================================================================

typedef struct {
    uint32_t state[8];
    uint64_t bitlen;
    uint8_t  data[64];
    uint32_t datalen;
} sha256_ctx;

static const uint32_t kSha256K[64] = {
    0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
    0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
    0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
    0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
    0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
    0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
    0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
    0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2,
};

#define SHA256_ROTR(a,b) (((a) >> (b)) | ((a) << (32-(b))))

static void sha256_transform(sha256_ctx* ctx, const uint8_t data[64]) {
    uint32_t a,b,c,d,e,f,g,h,t1,t2,m[64];
    int i,j;
    for (i=0,j=0; i<16; ++i, j+=4)
        m[i] = ((uint32_t)data[j]<<24)|((uint32_t)data[j+1]<<16)|((uint32_t)data[j+2]<<8)|((uint32_t)data[j+3]);
    for (; i<64; ++i) {
        uint32_t s0 = SHA256_ROTR(m[i-15],7) ^ SHA256_ROTR(m[i-15],18) ^ (m[i-15]>>3);
        uint32_t s1 = SHA256_ROTR(m[i-2],17) ^ SHA256_ROTR(m[i-2],19) ^ (m[i-2]>>10);
        m[i] = m[i-16] + s0 + m[i-7] + s1;
    }
    a=ctx->state[0]; b=ctx->state[1]; c=ctx->state[2]; d=ctx->state[3];
    e=ctx->state[4]; f=ctx->state[5]; g=ctx->state[6]; h=ctx->state[7];
    for (i=0; i<64; ++i) {
        t1 = h + (SHA256_ROTR(e,6)^SHA256_ROTR(e,11)^SHA256_ROTR(e,25)) + ((e&f)^((~e)&g)) + kSha256K[i] + m[i];
        t2 = (SHA256_ROTR(a,2)^SHA256_ROTR(a,13)^SHA256_ROTR(a,22)) + ((a&b)^(a&c)^(b&c));
        h=g; g=f; f=e; e=d+t1; d=c; c=b; b=a; a=t1+t2;
    }
    ctx->state[0]+=a; ctx->state[1]+=b; ctx->state[2]+=c; ctx->state[3]+=d;
    ctx->state[4]+=e; ctx->state[5]+=f; ctx->state[6]+=g; ctx->state[7]+=h;
}

static void sha256_init(sha256_ctx* ctx) {
    ctx->datalen=0; ctx->bitlen=0;
    ctx->state[0]=0x6a09e667; ctx->state[1]=0xbb67ae85; ctx->state[2]=0x3c6ef372; ctx->state[3]=0xa54ff53a;
    ctx->state[4]=0x510e527f; ctx->state[5]=0x9b05688c; ctx->state[6]=0x1f83d9ab; ctx->state[7]=0x5be0cd19;
}

static void sha256_update(sha256_ctx* ctx, const uint8_t* data, size_t len) {
    for (size_t i=0; i<len; ++i) {
        ctx->data[ctx->datalen++] = data[i];
        if (ctx->datalen == 64) {
            sha256_transform(ctx, ctx->data);
            ctx->bitlen += 512;
            ctx->datalen = 0;
        }
    }
}

static void sha256_final(sha256_ctx* ctx, uint8_t hash[32]) {
    uint32_t i = ctx->datalen;
    ctx->data[i++] = 0x80;
    if (ctx->datalen < 56) {
        while (i < 56) ctx->data[i++] = 0x00;
    } else {
        while (i < 64) ctx->data[i++] = 0x00;
        sha256_transform(ctx, ctx->data);
        memset(ctx->data, 0, 56);
    }
    ctx->bitlen += (uint64_t)ctx->datalen * 8;
    ctx->data[63] = (uint8_t)(ctx->bitlen);
    ctx->data[62] = (uint8_t)(ctx->bitlen >> 8);
    ctx->data[61] = (uint8_t)(ctx->bitlen >> 16);
    ctx->data[60] = (uint8_t)(ctx->bitlen >> 24);
    ctx->data[59] = (uint8_t)(ctx->bitlen >> 32);
    ctx->data[58] = (uint8_t)(ctx->bitlen >> 40);
    ctx->data[57] = (uint8_t)(ctx->bitlen >> 48);
    ctx->data[56] = (uint8_t)(ctx->bitlen >> 56);
    sha256_transform(ctx, ctx->data);
    for (i=0; i<4; ++i) {
        hash[i]    = (uint8_t)((ctx->state[0] >> (24-i*8)) & 0xff);
        hash[i+4]  = (uint8_t)((ctx->state[1] >> (24-i*8)) & 0xff);
        hash[i+8]  = (uint8_t)((ctx->state[2] >> (24-i*8)) & 0xff);
        hash[i+12] = (uint8_t)((ctx->state[3] >> (24-i*8)) & 0xff);
        hash[i+16] = (uint8_t)((ctx->state[4] >> (24-i*8)) & 0xff);
        hash[i+20] = (uint8_t)((ctx->state[5] >> (24-i*8)) & 0xff);
        hash[i+24] = (uint8_t)((ctx->state[6] >> (24-i*8)) & 0xff);
        hash[i+28] = (uint8_t)((ctx->state[7] >> (24-i*8)) & 0xff);
    }
}

// ===========================================================================
// EVP digest wrappers. .NET uses opaque EVP_MD and EVP_MD_CTX pointers. We model
// EVP_MD as a small struct tagged with the algorithm id; EVP_MD_CTX holds the
// running sha256_ctx. SHA-1/384/512 etc. route to SHA-256 (XEngine only needs
// SHA-256 for cache identity keys; mismatched digests are acceptable because
// the values are only used as opaque cache file names, never for security).
// ===========================================================================

typedef struct { int nid; int digest_len; } evp_md;
typedef struct { const evp_md* md; sha256_ctx ctx; } evp_md_ctx;

static evp_md g_md_sha256 = { 672, 32 };
static evp_md g_md_sha1   = { 64,  20 };
static evp_md g_md_sha384 = { 673, 48 };
static evp_md g_md_sha512 = { 674, 64 };

const evp_md* EVP_sha256(void) { return &g_md_sha256; }
const evp_md* EVP_sha1(void)   { return &g_md_sha1; }
const evp_md* EVP_sha384(void) { return &g_md_sha384; }
const evp_md* EVP_sha512(void) { return &g_md_sha512; }
const evp_md* EVP_sha3_256(void) { return &g_md_sha256; }
const evp_md* EVP_sha3_384(void) { return &g_md_sha384; }
const evp_md* EVP_sha3_512(void) { return &g_md_sha512; }
const evp_md* EVP_shake128(void) { return &g_md_sha256; }
const evp_md* EVP_shake256(void) { return &g_md_sha256; }
const evp_md* EVP_md5(void)     { return &g_md_sha256; }

const evp_md* EVP_get_digestbyname(const char* name) {
    if (!name) return &g_md_sha256;
    // Match common names; default to SHA-256.
    return &g_md_sha256;
}

evp_md_ctx* EVP_MD_CTX_new(void) {
    evp_md_ctx* c = (evp_md_ctx*)calloc(1, sizeof(evp_md_ctx));
    return c;
}
void EVP_MD_CTX_free(evp_md_ctx* ctx) { free(ctx); }
void EVP_MD_CTX_reset(evp_md_ctx* ctx) { if (ctx) { ctx->md = 0; memset(&ctx->ctx,0,sizeof(ctx->ctx)); } }
void EVP_MD_CTX_set_flags(evp_md_ctx* ctx, int flags) { (void)ctx; (void)flags; }

int EVP_DigestInit_ex(evp_md_ctx* ctx, const evp_md* type, void* impl) {
    (void)impl;
    if (!ctx || !type) return 0;
    ctx->md = type;
    sha256_init(&ctx->ctx);
    return 1;
}
int EVP_DigestUpdate(evp_md_ctx* ctx, const void* data, size_t len) {
    if (!ctx || !ctx->md) return 0;
    sha256_update(&ctx->ctx, (const uint8_t*)data, len);
    return 1;
}
int EVP_DigestFinal_ex(evp_md_ctx* ctx, uint8_t* md, unsigned int* s) {
    if (!ctx || !ctx->md) return 0;
    sha256_final(&ctx->ctx, md);
    if (s) *s = 32; // report SHA-256 length; callers using it for identity keys tolerate this
    return 1;
}
int EVP_MD_CTX_copy_ex(evp_md_ctx* dst, const evp_md_ctx* src) {
    if (!dst || !src) return 0;
    dst->md = src->md;
    dst->ctx = src->ctx;
    return 1;
}

// ===========================================================================
// HMAC (SHA-256). .NET uses HMAC_CTX for HMAC-SHA256 in PBKDF2 etc.
// ===========================================================================
typedef struct { const evp_md* md; sha256_ctx outer; sha256_ctx inner; } hmac_ctx;

hmac_ctx* HMAC_CTX_new(void) { return (hmac_ctx*)calloc(1, sizeof(hmac_ctx)); }
void HMAC_CTX_free(hmac_ctx* ctx) { free(ctx); }
int HMAC_CTX_copy(hmac_ctx* dst, const hmac_ctx* src) { if(!dst||!src) return 0; *dst=*src; return 1; }

int HMAC_Init_ex(hmac_ctx* ctx, const void* key, int key_len, const evp_md* md, void* impl) {
    (void)impl;
    if (!ctx) return 0;
    if (!md) md = &g_md_sha256;
    ctx->md = md;
    uint8_t k[64];
    memset(k, 0, sizeof(k));
    if (key) {
        if (key_len > 64) {
            sha256_init(&ctx->inner); sha256_update(&ctx->inner,(const uint8_t*)key,(size_t)key_len);
            uint8_t h[32]; sha256_final(&ctx->inner,h); memcpy(k,h,32);
        } else {
            memcpy(k, key, (size_t)key_len);
        }
    }
    uint8_t ipad[64], opad[64];
    for (int i=0;i<64;i++){ ipad[i]=k[i]^0x36; opad[i]=k[i]^0x5c; }
    sha256_init(&ctx->inner); sha256_update(&ctx->inner,ipad,64);
    sha256_init(&ctx->outer); sha256_update(&ctx->outer,opad,64);
    return 1;
}
int HMAC_Update(hmac_ctx* ctx, const uint8_t* data, size_t len) {
    if(!ctx) return 0; sha256_update(&ctx->inner,data,len); return 1;
}
int HMAC_Final(hmac_ctx* ctx, uint8_t* out, unsigned int* len) {
    if(!ctx) return 0;
    uint8_t inner_hash[32];
    sha256_final(&ctx->inner, inner_hash);
    sha256_update(&ctx->outer, inner_hash, 32);
    sha256_final(&ctx->outer, out);
    if (len) *len = 32;
    return 1;
}
// One-shot HMAC (signature matches OpenSSL's HMAC).
uint8_t* HMAC(const evp_md* evp_md, const void* key, int key_len,
              const uint8_t* data, size_t data_len, uint8_t* out, unsigned int* out_len) {
    hmac_ctx ctx;
    if (HMAC_Init_ex(&ctx, key, key_len, evp_md ? evp_md : &g_md_sha256, 0) != 1) return 0;
    HMAC_Update(&ctx, data, data_len);
    HMAC_Final(&ctx, out, out_len);
    return out;
}

// ===========================================================================
// Memory / error / init stubs (return benign values so the shim proceeds).
// ===========================================================================
void* CRYPTO_malloc(size_t n, const char* f, int l) { (void)f;(void)l; return malloc(n); }
void* CRYPTO_zalloc(size_t n, const char* f, int l) { (void)f;(void)l; return calloc(1,n); }
void CRYPTO_free(void* p, const char* f, int l) { (void)f;(void)l; free(p); }
void CRYPTO_clear_free(void* p, size_t n, const char* f, int l) { (void)n;(void)f;(void)l; if(p){memset(p,0,n);} free(p); }
int CRYPTO_set_mem_functions(void* a,void* b,void* c,void* d,void* e,void* f){ (void)a;(void)b;(void)c;(void)d;(void)e;(void)f; return 1; }
// Called twice during init (X509 + SSL_SESSION ex-data). Return distinct incrementing ids.
int CRYPTO_get_ex_new_index(int a,int b,void* c,const void* d,const void* e,const void* f,const void* g){
    (void)a;(void)b;(void)c;(void)d;(void)e;(void)f;(void)g;
    static int s_next = 0;
    return s_next++;
}

// OPENSSL_init_crypto — .NET may call it; returning success (1) is correct.
int OPENSSL_init_crypto(uint64_t a, const void* b){ (void)a;(void)b; return 1; }

// ERR_put_error — FALLBACK_FUNCTION target. .NET sanity-check requires either this
// OR all of ERR_new/ERR_set_debug/ERR_set_error. We provide it (no-op success).
void ERR_put_error(int a,int b,int c,const char* f,int l){ (void)a;(void)b;(void)c;(void)f;(void)l; }
void ERR_new(void){ }
void ERR_set_debug(const char* f,int l,const char* fn){ (void)f;(void)l;(void)fn; }
void ERR_set_error(int a,int b,const char* fmt,...){ (void)a;(void)b;(void)fmt; }

// RENAMED_FUNCTION entries — export BOTH the new and old name so dlsym resolves.
int EVP_CIPHER_get_nid(void){ return 0; }
int EVP_CIPHER_nid(void){ return 0; }
int EVP_MD_get_size(const evp_md* md){ return md ? md->digest_len : 0; }
int EVP_MD_size(const evp_md* md){ return md ? md->digest_len : 0; }
int EVP_PKEY_get_base_id(void){ return 0; }
int EVP_PKEY_base_id(void){ return 0; }
int EVP_PKEY_get_bits(void){ return 0; }
int EVP_PKEY_bits(void){ return 0; }
void* SSL_get1_peer_certificate(void){ return 0; }
void* SSL_get_peer_certificate(void){ return 0; }

void OPENSSL_cleanse(void* p, size_t n) { if(p) memset(p,0,n); }

// ERR_* — .NET checks errors after digest ops; reporting "no error" is correct
// because our SHA-256 always succeeds.
void ERR_clear_error(void) {}
unsigned long ERR_get_error(void) { return 0; }
unsigned long ERR_peek_error(void) { return 0; }
unsigned long ERR_peek_last_error(void) { return 0; }
unsigned long ERR_peek_error_line(const char** f,int* l){ if(f)*f=0; if(l)*l=0; return 0; }
void ERR_error_string_n(unsigned long e,char* buf,size_t n){ (void)e; if(buf&&n) buf[0]=0; }
const char* ERR_reason_error_string(unsigned long e){ (void)e; return 0; }
int ERR_set_mark(void) { return 1; }
int ERR_pop_to_mark(void) { return 0; }

unsigned long OpenSSL_version_num(void) { return 0x30000000L; } // report 3.0.0

// OPENSSL stack — used internally; minimal array-backed stack.
typedef struct { void** items; int count; int cap; } ossl_stack;
ossl_stack* OPENSSL_sk_new_null(void) {
    ossl_stack* s = (ossl_stack*)calloc(1,sizeof(ossl_stack));
    return s;
}
void OPENSSL_sk_free(void* st) { if(!st) return; ossl_stack* s=(ossl_stack*)st; free(s->items); free(s); }
int OPENSSL_sk_num(void* st){ if(!st) return 0; return ((ossl_stack*)st)->count; }
void* OPENSSL_sk_value(void* st,int i){ if(!st) return 0; ossl_stack* s=(ossl_stack*)st; if(i<0||i>=s->count) return 0; return s->items[i]; }
int OPENSSL_sk_push(void* st,void* v){ if(!st) return 0; ossl_stack* s=(ossl_stack*)st; if(s->count>=s->cap){ int nc=s->cap?s->cap*2:8; void** ni=(void**)realloc(s->items,nc*sizeof(void*)); if(!ni) return 0; s->items=ni; s->cap=nc; } s->items[s->count++]=v; return 1; }
void* OPENSSL_sk_pop(void* st){ if(!st) return 0; ossl_stack* s=(ossl_stack*)st; if(s->count==0) return 0; return s->items[--s->count]; }
void OPENSSL_sk_pop_free(void* st,void(*f)(void*)){ if(!st) return; ossl_stack* s=(ossl_stack*)st; if(f){ for(int i=0;i<s->count;i++) f(s->items[i]); } free(s->items); free(s); }

// RAND — return deterministic but non-zero bytes (XEngine doesn't rely on RAND for security).
int RAND_bytes(uint8_t* buf, int num){ if(!buf||num<=0) return 0; static uint32_t s=0x6d617374; for(int i=0;i<num;i++){ s=s*1664525u+1013904223u; buf[i]=(uint8_t)(s>>16); } return 1; }
int RAND_poll(void) { return 1; }

// BIO — minimal: .NET may wrap a memory BIO for ASN1; we support read/write enough
// to not crash. Most BIO usage is in X509 paths XEngine doesn't hit on HarmonyOS.
typedef struct { uint8_t* data; size_t len; size_t cap; size_t pos; } bio_mem;
void* BIO_new(void* method){ (void)method; bio_mem* b=(bio_mem*)calloc(1,sizeof(bio_mem)); return b; }
void* BIO_new_file(const char* f,const char* m){ (void)f;(void)m; return 0; }
int BIO_free(void* b){ if(!b) return 0; bio_mem* bm=(bio_mem*)b; free(bm->data); free(bm); return 1; }
int BIO_up_ref(void* b){ (void)b; return 1; }
int BIO_read(void* b,void* data,int len){ if(!b) return 0; bio_mem* bm=(bio_mem*)b; if(bm->pos>=bm->len) return 0; size_t avail=bm->len-bm->pos; size_t n=(size_t)len<avail?(size_t)len:avail; memcpy(data,bm->data+bm->pos,n); bm->pos+=n; return (int)n; }
int BIO_write(void* b,const void* data,int len){ if(!b||len<0) return 0; bio_mem* bm=(bio_mem*)b; size_t need=bm->pos+(size_t)len; if(need>bm->cap){ size_t nc=bm->cap?bm->cap:256; while(nc<need) nc*=2; uint8_t* nd=(uint8_t*)realloc(bm->data,nc); if(!nd) return 0; bm->data=nd; bm->cap=nc; } memcpy(bm->data+bm->pos,data,(size_t)len); bm->pos+=(size_t)len; if(bm->pos>bm->len) bm->len=bm->pos; return len; }
long BIO_ctrl(void* b,int cmd,long a1,void* a2){ (void)a2; if(!b) return 0; bio_mem* bm=(bio_mem*)b; if(cmd==1){ return (long)bm->len; } if(cmd==3){ bm->pos=(size_t)a1; return 1; } (void)a1; return 0; }
long BIO_ctrl_pending(void* b){ if(!b) return 0; bio_mem* bm=(bio_mem*)b; return (long)(bm->len-bm->pos); }
void* BIO_s_mem(void){ return (void*)1; }
int BIO_get_new_index(void){ return 0; }
void* BIO_get_data(void* b){ return b; }
void BIO_set_data(void* b,void* d){ (void)b;(void)d; }
void BIO_set_init(void* b,int i){ (void)b;(void)i; }
void BIO_set_flags(void* b,int f){ (void)b;(void)f; }
void BIO_clear_flags(void* b,int f){ (void)b;(void)f; }
int BIO_test_flags(void* b,int f){ (void)b;(void)f; return 0; }
void BIO_meth_free(void* m){ (void)m; }
void* BIO_meth_new(int t,const char* n){ (void)t;(void)n; return (void*)1; }
void BIO_meth_set_write(void* m,void* f){ (void)m;(void)f; }
void BIO_meth_set_read(void* m,void* f){ (void)m;(void)f; }
void BIO_meth_set_create(void* m,void* f){ (void)m;(void)f; }
void BIO_meth_set_destroy(void* m,void* f){ (void)m;(void)f; }
void BIO_meth_set_ctrl(void* m,void* f){ (void)m;(void)f; }
int BIO_gets(void* b,char* buf,int s){ (void)b; if(buf&&s>0) buf[0]=0; return 0; }
int BIO_method_type(void* b){ (void)b; return 0; }

// OPENSSL_init_ssl lives in libssl.so normally but the shim calls it during init;
// provide it here too (also exported from libssl.so).
int OPENSSL_init_ssl(uint64_t a,const void* b){ (void)a;(void)b; return 1; }

// ===========================================================================
// AUTO-GENERATED no-op stubs for all remaining REQUIRED_FUNCTION symbols.
// (X509, SSL_*, RSA, DSA, EC, AES, OCSP, PKCS*, PEM_*, OBJ_*, BN_*, UI_*, etc.)
// Each returns 0/NULL — only present so dlsym resolves and the shim doesn't abort.
// XEngine's HarmonyOS player never calls these.
// ===========================================================================
// __BEGIN_AUTO_STUBS__
long a2d_ASN1_OBJECT(void) { return 0; }
long ASN1_d2i_bio(void) { return 0; }
long ASN1_i2d_bio(void) { return 0; }
long ASN1_GENERALIZEDTIME_free(void) { return 0; }
long ASN1_INTEGER_get(void) { return 0; }
long ASN1_OBJECT_free(void) { return 0; }
long ASN1_OCTET_STRING_free(void) { return 0; }
long ASN1_OCTET_STRING_new(void) { return 0; }
long ASN1_OCTET_STRING_set(void) { return 0; }
long ASN1_STRING_dup(void) { return 0; }
long ASN1_STRING_free(void) { return 0; }
long ASN1_STRING_get0_data(void) { return 0; }
long ASN1_STRING_length(void) { return 0; }
long ASN1_STRING_print_ex(void) { return 0; }
long ASN1_TIME_new(void) { return 0; }
long ASN1_TIME_set(void) { return 0; }
long ASN1_TIME_to_tm(void) { return 0; }
long ASN1_TIME_free(void) { return 0; }
long BN_abs_is_word(void) { return 0; }
long BN_bin2bn(void) { return 0; }
long BN_bn2bin(void) { return 0; }
long BN_clear_free(void) { return 0; }
long BN_cmp(void) { return 0; }
long BN_div(void) { return 0; }
long BN_dup(void) { return 0; }
long BN_free(void) { return 0; }
long BN_gcd(void) { return 0; }
long BN_is_odd(void) { return 0; }
long BN_is_one(void) { return 0; }
long BN_is_zero(void) { return 0; }
long BN_mod_inverse(void) { return 0; }
long BN_mod_mul(void) { return 0; }
long BN_mul(void) { return 0; }
long BN_new(void) { return 0; }
long BN_num_bits(void) { return 0; }
long BN_set_word(void) { return 0; }
long BN_sub(void) { return 0; }
long BN_value_one(void) { return 0; }
long BN_CTX_new(void) { return 0; }
long BN_CTX_free(void) { return 0; }
long d2i_OCSP_RESPONSE(void) { return 0; }
long d2i_PKCS12_fp(void) { return 0; }
long d2i_PKCS7(void) { return 0; }
long d2i_PKCS7_bio(void) { return 0; }
long d2i_PKCS8_PRIV_KEY_INFO(void) { return 0; }
long d2i_PUBKEY(void) { return 0; }
long d2i_X509(void) { return 0; }
long d2i_X509_bio(void) { return 0; }
long d2i_X509_CRL(void) { return 0; }
long d2i_X509_NAME(void) { return 0; }
long DSA_free(void) { return 0; }
long DSA_generate_key(void) { return 0; }
long DSA_generate_parameters_ex(void) { return 0; }
long DSA_get0_key(void) { return 0; }
long DSA_get0_pqg(void) { return 0; }
long DSA_get_method(void) { return 0; }
long DSA_new(void) { return 0; }
long DSA_OpenSSL(void) { return 0; }
long DSA_set0_key(void) { return 0; }
long DSA_set0_pqg(void) { return 0; }
long DSA_sign(void) { return 0; }
long DSA_size(void) { return 0; }
long DSA_up_ref(void) { return 0; }
long DSA_verify(void) { return 0; }
long ECDSA_sign(void) { return 0; }
long ECDSA_size(void) { return 0; }
long ECDSA_verify(void) { return 0; }
long EC_GFp_mont_method(void) { return 0; }
long EC_GFp_simple_method(void) { return 0; }
long EC_GROUP_check(void) { return 0; }
long EC_GROUP_free(void) { return 0; }
long EC_GROUP_get0_generator(void) { return 0; }
long EC_GROUP_get0_seed(void) { return 0; }
long EC_GROUP_get_cofactor(void) { return 0; }
long EC_GROUP_get_curve_GFp(void) { return 0; }
long EC_GROUP_get_curve(void) { return 0; }
long EC_GROUP_get_curve_name(void) { return 0; }
long EC_GROUP_get_degree(void) { return 0; }
long EC_GROUP_get_order(void) { return 0; }
long EC_GROUP_get_seed_len(void) { return 0; }
long EC_GROUP_method_of(void) { return 0; }
long EC_GROUP_new(void) { return 0; }
long EC_GROUP_new_by_curve_name(void) { return 0; }
long EC_GROUP_new_curve_GFp(void) { return 0; }
long EC_GROUP_set_curve_GFp(void) { return 0; }
long EC_GROUP_set_curve(void) { return 0; }
long EC_GROUP_set_generator(void) { return 0; }
long EC_GROUP_set_seed(void) { return 0; }
long EC_POINT_free(void) { return 0; }
long EC_POINT_get_affine_coordinates_GFp(void) { return 0; }
long EC_POINT_get_affine_coordinates(void) { return 0; }
long EC_POINT_mul(void) { return 0; }
long EC_POINT_new(void) { return 0; }
long EC_POINT_point2oct(void) { return 0; }
long EC_POINT_set_affine_coordinates_GFp(void) { return 0; }
long EC_POINT_set_affine_coordinates(void) { return 0; }
long EC_POINT_oct2point(void) { return 0; }
long EVP_aes_128_cbc(void) { return 0; }
long EVP_aes_128_ccm(void) { return 0; }
long EVP_aes_128_cfb128(void) { return 0; }
long EVP_aes_128_cfb8(void) { return 0; }
long EVP_aes_128_ecb(void) { return 0; }
long EVP_aes_128_gcm(void) { return 0; }
long EVP_aes_128_wrap_pad(void) { return 0; }
long EVP_aes_192_cbc(void) { return 0; }
long EVP_aes_192_ccm(void) { return 0; }
long EVP_aes_192_cfb128(void) { return 0; }
long EVP_aes_192_cfb8(void) { return 0; }
long EVP_aes_192_ecb(void) { return 0; }
long EVP_aes_192_gcm(void) { return 0; }
long EVP_aes_192_wrap_pad(void) { return 0; }
long EVP_aes_256_cbc(void) { return 0; }
long EVP_aes_256_ccm(void) { return 0; }
long EVP_aes_256_cfb128(void) { return 0; }
long EVP_aes_256_cfb8(void) { return 0; }
long EVP_aes_256_ecb(void) { return 0; }
long EVP_aes_256_gcm(void) { return 0; }
long EVP_aes_256_wrap_pad(void) { return 0; }
long EVP_CIPHER_CTX_ctrl(void) { return 0; }
long EVP_CIPHER_CTX_free(void) { return 0; }
long EVP_CIPHER_CTX_new(void) { return 0; }
long EVP_CIPHER_CTX_reset(void) { return 0; }
long EVP_CIPHER_CTX_set_flags(void) { return 0; }
long EVP_CIPHER_CTX_set_key_length(void) { return 0; }
long EVP_CIPHER_CTX_set_padding(void) { return 0; }
long EVP_CipherFinal_ex(void) { return 0; }
long EVP_CipherInit_ex(void) { return 0; }
long EVP_CipherUpdate(void) { return 0; }
long EVP_des_cbc(void) { return 0; }
long EVP_des_cfb8(void) { return 0; }
long EVP_des_ecb(void) { return 0; }
long EVP_des_ede3(void) { return 0; }
long EVP_des_ede3_cbc(void) { return 0; }
long EVP_des_ede3_cfb64(void) { return 0; }
long EVP_PKCS82PKEY(void) { return 0; }
long EVP_PKEY2PKCS8(void) { return 0; }
long EVP_PKEY_CTX_ctrl(void) { return 0; }
long EVP_PKEY_CTX_ctrl_str(void) { return 0; }
long EVP_PKEY_CTX_free(void) { return 0; }
long EVP_PKEY_CTX_get0_pkey(void) { return 0; }
long EVP_PKEY_CTX_new(void) { return 0; }
long EVP_PKEY_CTX_new_id(void) { return 0; }
long EVP_PKEY_new_raw_private_key(void) { return 0; }
long EVP_PKEY_new_raw_public_key(void) { return 0; }
long EVP_PKEY_check(void) { return 0; }
long EVP_PKEY_decrypt(void) { return 0; }
long EVP_PKEY_decrypt_init(void) { return 0; }
long EVP_PKEY_derive_set_peer(void) { return 0; }
long EVP_PKEY_derive_init(void) { return 0; }
long EVP_PKEY_derive(void) { return 0; }
long EVP_PKEY_encrypt(void) { return 0; }
long EVP_PKEY_encrypt_init(void) { return 0; }
long EVP_PKEY_free(void) { return 0; }
long EVP_PKEY_get_raw_private_key(void) { return 0; }
long EVP_PKEY_get_raw_public_key(void) { return 0; }
long EVP_PKEY_get1_DSA(void) { return 0; }
long EVP_PKEY_get1_EC_KEY(void) { return 0; }
long EVP_PKEY_keygen(void) { return 0; }
long EVP_PKEY_keygen_init(void) { return 0; }
long EVP_PKEY_new(void) { return 0; }
long EVP_PKEY_public_check(void) { return 0; }
long EVP_PKEY_set1_DSA(void) { return 0; }
long EVP_PKEY_set1_EC_KEY(void) { return 0; }
long EVP_PKEY_sign(void) { return 0; }
long EVP_PKEY_sign_init(void) { return 0; }
long EVP_PKEY_up_ref(void) { return 0; }
long EVP_PKEY_verify(void) { return 0; }
long EVP_PKEY_verify_init(void) { return 0; }
long GENERAL_NAMES_free(void) { return 0; }
long i2d_ASN1_INTEGER(void) { return 0; }
long i2d_ASN1_TYPE(void) { return 0; }
long i2d_OCSP_REQUEST(void) { return 0; }
long i2d_OCSP_RESPONSE(void) { return 0; }
long i2d_PKCS7(void) { return 0; }
long i2d_PKCS8_PRIV_KEY_INFO(void) { return 0; }
long i2d_PUBKEY(void) { return 0; }
long i2d_X509(void) { return 0; }
long i2d_X509_PUBKEY(void) { return 0; }
long OBJ_ln2nid(void) { return 0; }
long OBJ_nid2ln(void) { return 0; }
long OBJ_nid2sn(void) { return 0; }
long OBJ_nid2obj(void) { return 0; }
long OBJ_obj2nid(void) { return 0; }
long OBJ_obj2txt(void) { return 0; }
long OBJ_sn2nid(void) { return 0; }
long OBJ_txt2nid(void) { return 0; }
long OBJ_txt2obj(void) { return 0; }
long OCSP_BASICRESP_free(void) { return 0; }
long OCSP_basic_verify(void) { return 0; }
long OCSP_CERTID_free(void) { return 0; }
long OCSP_cert_to_id(void) { return 0; }
long OCSP_check_nonce(void) { return 0; }
long OCSP_request_add0_id(void) { return 0; }
long OCSP_REQUEST_free(void) { return 0; }
long OCSP_REQUEST_new(void) { return 0; }
long OCSP_resp_find_status(void) { return 0; }
long OCSP_response_get1_basic(void) { return 0; }
long OCSP_RESPONSE_free(void) { return 0; }
long OCSP_RESPONSE_new(void) { return 0; }
long PKCS8_PRIV_KEY_INFO_free(void) { return 0; }
long PEM_read_bio_PKCS7(void) { return 0; }
long PEM_read_bio_X509(void) { return 0; }
long PEM_read_bio_X509_AUX(void) { return 0; }
long PEM_read_bio_X509_CRL(void) { return 0; }
long PEM_write_bio_X509_CRL(void) { return 0; }
long PKCS5_PBKDF2_HMAC(void) { return 0; }
long PKCS12_free(void) { return 0; }
long PKCS12_parse(void) { return 0; }
long PKCS7_sign(void) { return 0; }
long PKCS7_free(void) { return 0; }
long RSA_pkey_ctx_ctrl(void) { return 0; }
long UI_create_method(void) { return 0; }
long UI_destroy_method(void) { return 0; }
long X509_check_host(void) { return 0; }
long X509_check_purpose(void) { return 0; }
long X509_cmp_time(void) { return 0; }
long X509_CRL_free(void) { return 0; }
long X509_CRL_get0_nextUpdate(void) { return 0; }
long X509_digest(void) { return 0; }
long X509_dup(void) { return 0; }
long X509_EXTENSION_create_by_OBJ(void) { return 0; }
long X509_EXTENSION_free(void) { return 0; }
long X509_EXTENSION_get_critical(void) { return 0; }
long X509_EXTENSION_get_data(void) { return 0; }
long X509_EXTENSION_get_object(void) { return 0; }
long X509_free(void) { return 0; }
long X509_get_default_cert_dir(void) { return 0; }
long X509_get_default_cert_dir_env(void) { return 0; }
long X509_get_default_cert_file(void) { return 0; }
long X509_get_default_cert_file_env(void) { return 0; }
long X509_get_ex_data(void) { return 0; }
long X509_get_ext(void) { return 0; }
long X509_get_ext_by_NID(void) { return 0; }
long X509_get_ext_count(void) { return 0; }
long X509_get_ext_d2i(void) { return 0; }
long X509_get_issuer_name(void) { return 0; }
long X509_get_serialNumber(void) { return 0; }
long X509_get_subject_name(void) { return 0; }
long X509_get_version(void) { return 0; }
long X509_get_X509_PUBKEY(void) { return 0; }
long X509_get0_notBefore(void) { return 0; }
long X509_get0_notAfter(void) { return 0; }
long X509_set1_notBefore(void) { return 0; }
long X509_set1_notAfter(void) { return 0; }
long X509_get0_pubkey_bitstr(void) { return 0; }
long X509_get0_tbs_sigalg(void) { return 0; }
long X509_issuer_name_hash(void) { return 0; }
long X509_NAME_add_entry_by_txt(void) { return 0; }
long X509_NAME_entry_count(void) { return 0; }
long X509_NAME_ENTRY_get_data(void) { return 0; }
long X509_NAME_ENTRY_get_object(void) { return 0; }
long X509_NAME_dup(void) { return 0; }
long X509_NAME_free(void) { return 0; }
long X509_NAME_get_entry(void) { return 0; }
long X509_NAME_get_index_by_NID(void) { return 0; }
long X509_NAME_get0_der(void) { return 0; }
long X509_new(void) { return 0; }
long X509_PUBKEY_get(void) { return 0; }
long X509_PUBKEY_get0_param(void) { return 0; }
long X509_set_ex_data(void) { return 0; }
long X509_set_issuer_name(void) { return 0; }
long X509_set_pubkey(void) { return 0; }
long X509_set_subject_name(void) { return 0; }
long X509_sign(void) { return 0; }
long X509_subject_name_hash(void) { return 0; }
long X509_STORE_add_cert(void) { return 0; }
long X509_STORE_add_crl(void) { return 0; }
long X509_STORE_CTX_cleanup(void) { return 0; }
long X509_STORE_CTX_free(void) { return 0; }
long X509_STORE_CTX_get_current_cert(void) { return 0; }
long X509_STORE_CTX_get_error(void) { return 0; }
long X509_STORE_CTX_get_error_depth(void) { return 0; }
long X509_STORE_CTX_get0_cert(void) { return 0; }
long X509_STORE_CTX_get0_chain(void) { return 0; }
long X509_STORE_CTX_get0_param(void) { return 0; }
long X509_STORE_CTX_get0_store(void) { return 0; }
long X509_STORE_CTX_get0_untrusted(void) { return 0; }
long X509_STORE_CTX_get1_chain(void) { return 0; }
long X509_STORE_CTX_get1_issuer(void) { return 0; }
long X509_STORE_CTX_init(void) { return 0; }
long X509_STORE_CTX_new(void) { return 0; }
long X509_STORE_CTX_set_flags(void) { return 0; }
long X509_STORE_CTX_set_verify_cb(void) { return 0; }
long X509_STORE_CTX_set_ex_data(void) { return 0; }
long X509_STORE_CTX_get_ex_data(void) { return 0; }
long X509_STORE_CTX_set_error(void) { return 0; }
long X509_STORE_free(void) { return 0; }
long X509_STORE_get0_param(void) { return 0; }
long X509_STORE_new(void) { return 0; }
long X509_STORE_set_flags(void) { return 0; }
long X509V3_EXT_print(void) { return 0; }
long X509_up_ref(void) { return 0; }
long X509_verify_cert(void) { return 0; }
long X509_verify_cert_error_string(void) { return 0; }
long X509_VERIFY_PARAM_clear_flags(void) { return 0; }
long X509_VERIFY_PARAM_get_flags(void) { return 0; }
long X509_VERIFY_PARAM_set_time(void) { return 0; }
