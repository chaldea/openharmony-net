#!/usr/bin/env python3
"""Generate a minimal libcrypto.so stub for HarmonyOS that lets .NET NativeAOT's
System.Security.Cryptography.Native shim resolve all 454 REQUIRED_FUNCTION symbols
via dlsym, while providing a REAL SHA-256 implementation for the EVP digest path.

Background: .NET's opensslshim.c calls OpenLibrary("libssl.so"), which the dynamic
linker resolves together with its libcrypto.so dependency. It then dlsyms every
symbol listed under REQUIRED_FUNCTION in opensslshim.h (454 of them). If any is
missing, it aborts. It then calls the EVP_* digest functions to compute hashes.

This stub exports all 454 symbols. The EVP digest family (EVP_MD_CTX_new/free,
EVP_DigestInit_ex, EVP_DigestUpdate, EVP_DigestFinal_ex, EVP_sha256/sha1/sha384/
sha512, EVP_get_digestbyname, EVP_MD_CTX_copy_ex) plus HMAC have real working
implementations backed by a pure-C SHA-256. Every other symbol is a no-op stub
that returns 0/NULL (they are only needed to satisfy dlsym; XEngine's HarmonyOS
player does not use X509/SSL/RSA/AES at runtime — only SHA-256 for cache keys).

A companion libssl.so re-exports the SSL_* symbols and depends on libcrypto.so so
that dlopen("libssl.so") pulls in libcrypto.so automatically.
"""
import os, sys, textwrap

HERE = os.path.dirname(os.path.abspath(__file__))

# All 454 REQUIRED_FUNCTION symbol names from dotnet/runtime opensslshim.h
# (extracted from the FOR_ALL_OPENSSL_FUNCTIONS REQUIRED_FUNCTION entries).
ALL_SYMBOLS = """a2d_ASN1_OBJECT ASN1_d2i_bio ASN1_i2d_bio ASN1_GENERALIZEDTIME_free
ASN1_INTEGER_get ASN1_OBJECT_free ASN1_OCTET_STRING_free ASN1_OCTET_STRING_new
ASN1_OCTET_STRING_set ASN1_STRING_dup ASN1_STRING_free ASN1_STRING_get0_data
ASN1_STRING_length ASN1_STRING_print_ex ASN1_TIME_new ASN1_TIME_set ASN1_TIME_to_tm
ASN1_TIME_free BIO_clear_flags BIO_ctrl BIO_ctrl_pending BIO_free BIO_get_data
BIO_get_new_index BIO_gets BIO_meth_free BIO_meth_new BIO_meth_set_create
BIO_meth_set_ctrl BIO_meth_set_destroy BIO_meth_set_read BIO_meth_set_write
BIO_method_type BIO_new BIO_new_file BIO_read BIO_set_data BIO_set_flags BIO_set_init
BIO_test_flags BIO_up_ref BIO_s_mem BIO_write BN_abs_is_word BN_bin2bn BN_bn2bin
BN_clear_free BN_cmp BN_div BN_dup BN_free BN_gcd BN_is_odd BN_is_one BN_is_zero
BN_mod_inverse BN_mod_mul BN_mul BN_new BN_num_bits BN_set_word BN_sub BN_value_one
BN_CTX_new BN_CTX_free CRYPTO_clear_free CRYPTO_free CRYPTO_get_ex_new_index
CRYPTO_malloc CRYPTO_set_mem_functions CRYPTO_zalloc d2i_OCSP_RESPONSE d2i_PKCS12_fp
d2i_PKCS7 d2i_PKCS7_bio d2i_PKCS8_PRIV_KEY_INFO d2i_PUBKEY d2i_X509 d2i_X509_bio
d2i_X509_CRL d2i_X509_NAME DSA_free DSA_generate_key DSA_generate_parameters_ex
DSA_get0_key DSA_get0_pqg DSA_get_method DSA_new DSA_OpenSSL DSA_set0_key
DSA_set0_pqg DSA_sign DSA_size DSA_up_ref DSA_verify ECDSA_sign ECDSA_size
ECDSA_verify EC_GFp_mont_method EC_GFp_simple_method EC_GROUP_check EC_GROUP_free
EC_GROUP_get0_generator EC_GROUP_get0_seed EC_GROUP_get_cofactor EC_GROUP_get_curve_GFp
EC_GROUP_get_curve EC_GROUP_get_curve_name EC_GROUP_get_degree EC_GROUP_get_order
EC_GROUP_get_seed_len EC_GROUP_method_of EC_GROUP_new EC_GROUP_new_by_curve_name
EC_GROUP_new_curve_GFp EC_GROUP_set_curve_GFp EC_GROUP_set_curve EC_GROUP_set_generator
EC_GROUP_set_seed EC_POINT_free EC_POINT_get_affine_coordinates_GFp
EC_POINT_get_affine_coordinates EC_POINT_mul EC_POINT_new EC_POINT_point2oct
EC_POINT_set_affine_coordinates_GFp EC_POINT_set_affine_coordinates EC_POINT_oct2point
ERR_clear_error ERR_error_string_n ERR_get_error ERR_peek_error ERR_peek_error_line
ERR_peek_last_error ERR_pop_to_mark ERR_reason_error_string ERR_set_mark
EVP_aes_128_cbc EVP_aes_128_ccm EVP_aes_128_cfb128 EVP_aes_128_cfb8 EVP_aes_128_ecb
EVP_aes_128_gcm EVP_aes_128_wrap_pad EVP_aes_192_cbc EVP_aes_192_ccm EVP_aes_192_cfb128
EVP_aes_192_cfb8 EVP_aes_192_ecb EVP_aes_192_gcm EVP_aes_192_wrap_pad EVP_aes_256_cbc
EVP_aes_256_ccm EVP_aes_256_cfb128 EVP_aes_256_cfb8 EVP_aes_256_ecb EVP_aes_256_gcm
EVP_aes_256_wrap_pad EVP_CIPHER_CTX_ctrl EVP_CIPHER_CTX_free EVP_CIPHER_CTX_new
EVP_CIPHER_CTX_reset EVP_CIPHER_CTX_set_flags EVP_CIPHER_CTX_set_key_length
EVP_CIPHER_CTX_set_padding EVP_CipherFinal_ex EVP_CipherInit_ex EVP_CipherUpdate
EVP_des_cbc EVP_des_cfb8 EVP_des_ecb EVP_des_ede3 EVP_des_ede3_cbc EVP_des_ede3_cfb64
EVP_DigestFinal_ex EVP_DigestInit_ex EVP_DigestUpdate EVP_get_digestbyname EVP_md5
EVP_MD_CTX_copy_ex EVP_MD_CTX_free EVP_MD_CTX_new EVP_MD_CTX_set_flags EVP_PKCS82PKEY
EVP_PKEY2PKCS8 EVP_PKEY_CTX_ctrl EVP_PKEY_CTX_ctrl_str EVP_PKEY_CTX_free
EVP_PKEY_CTX_get0_pkey EVP_PKEY_CTX_new EVP_PKEY_CTX_new_id EVP_PKEY_new_raw_private_key
EVP_PKEY_new_raw_public_key EVP_PKEY_check EVP_PKEY_decrypt EVP_PKEY_decrypt_init
EVP_PKEY_derive_set_peer EVP_PKEY_derive_init EVP_PKEY_derive EVP_PKEY_encrypt
EVP_PKEY_encrypt_init EVP_PKEY_free EVP_PKEY_get_raw_private_key
EVP_PKEY_get_raw_public_key EVP_PKEY_get1_DSA EVP_PKEY_get1_EC_KEY EVP_PKEY_keygen
EVP_PKEY_keygen_init EVP_PKEY_new EVP_PKEY_public_check EVP_PKEY_set1_DSA
EVP_PKEY_set1_EC_KEY EVP_PKEY_sign EVP_PKEY_sign_init EVP_PKEY_up_ref EVP_PKEY_verify
EVP_PKEY_verify_init EVP_sha1 EVP_sha256 EVP_sha384 EVP_sha512 EVP_sha3_256 EVP_sha3_384
EVP_sha3_512 EVP_shake128 EVP_shake256 GENERAL_NAMES_free HMAC HMAC_CTX_copy
HMAC_CTX_free HMAC_CTX_new HMAC_Final HMAC_Init_ex HMAC_Update i2d_ASN1_INTEGER
i2d_ASN1_TYPE i2d_OCSP_REQUEST i2d_OCSP_RESPONSE i2d_PKCS7 i2d_PKCS8_PRIV_KEY_INFO
i2d_PUBKEY i2d_X509 i2d_X509_PUBKEY OBJ_ln2nid OBJ_nid2ln OBJ_nid2sn OBJ_nid2obj
OBJ_obj2nid OBJ_obj2txt OBJ_sn2nid OBJ_txt2nid OBJ_txt2obj OCSP_BASICRESP_free
OCSP_basic_verify OCSP_CERTID_free OCSP_cert_to_id OCSP_check_nonce OCSP_request_add0_id
OCSP_REQUEST_free OCSP_REQUEST_new OCSP_resp_find_status OCSP_response_get1_basic
OCSP_RESPONSE_free OCSP_RESPONSE_new OPENSSL_cleanse OPENSSL_init_ssl OPENSSL_sk_free
OPENSSL_sk_new_null OPENSSL_sk_num OPENSSL_sk_pop OPENSSL_sk_pop_free OPENSSL_sk_push
OPENSSL_sk_value OpenSSL_version_num PKCS8_PRIV_KEY_INFO_free PEM_read_bio_PKCS7
PEM_read_bio_X509 PEM_read_bio_X509_AUX PEM_read_bio_X509_CRL PEM_write_bio_X509_CRL
PKCS5_PBKDF2_HMAC PKCS12_free PKCS12_parse PKCS7_sign PKCS7_free RAND_bytes RAND_poll
RSA_pkey_ctx_ctrl SSL_CIPHER_get_bits SSL_CIPHER_get_id SSL_ctrl SSL_add_client_CA
SSL_set_alpn_protos SSL_set_quiet_shutdown SSL_CTX_callback_ctrl SSL_CTX_check_private_key
SSL_CTX_config SSL_CTX_ctrl SSL_CTX_free SSL_CTX_get_ex_data SSL_is_init_finished
SSL_CTX_new SSL_CTX_sess_set_new_cb SSL_CTX_sess_set_remove_cb SSL_CTX_remove_session
SSL_CTX_set_cipher_list SSL_CTX_set_client_cert_cb SSL_CTX_set_ex_data
SSL_CTX_set_keylog_callback SSL_CTX_set_quiet_shutdown SSL_CTX_set_options
SSL_CTX_set_security_level SSL_CTX_set_session_id_context SSL_CTX_set_verify
SSL_CTX_set_cert_verify_callback SSL_CTX_use_certificate SSL_CTX_use_PrivateKey
SSL_do_handshake SSL_free SSL_get_ciphers SSL_get_sigalgs SSL_get_client_CA_list
SSL_get_current_cipher SSL_get_error SSL_get_ex_data SSL_get_ex_data_X509_STORE_CTX_idx
SSL_get_pending_cipher SSL_get_finished SSL_get_peer_cert_chain SSL_get_peer_finished
SSL_get_servername SSL_get_SSL_CTX SSL_get_version SSL_get_certificate SSL_new SSL_peek
SSL_pending SSL_get_rbio SSL_get_wbio SSL_read SSL_renegotiate SSL_renegotiate_pending
SSL_SESSION_free SSL_SESSION_get_ex_data SSL_SESSION_set_ex_data SSL_SESSION_get0_hostname
SSL_SESSION_set1_hostname SSL_session_reused SSL_set_accept_state SSL_set_bio
SSL_set_cert_cb SSL_set_cipher_list SSL_set_connect_state SSL_set_ex_data SSL_set_fd
SSL_set_options SSL_set_session SSL_get_session SSL_set_verify SSL_shutdown TLS_method
SSL_write SSL_use_certificate SSL_use_PrivateKey SSL_verify_client_post_handshake
SSL_set_post_handshake_auth SSL_version UI_create_method UI_destroy_method X509_check_host
X509_check_purpose X509_cmp_time X509_CRL_free X509_CRL_get0_nextUpdate X509_digest
X509_dup X509_EXTENSION_create_by_OBJ X509_EXTENSION_free X509_EXTENSION_get_critical
X509_EXTENSION_get_data X509_EXTENSION_get_object X509_free X509_get_default_cert_dir
X509_get_default_cert_dir_env X509_get_default_cert_file X509_get_default_cert_file_env
X509_get_ex_data X509_get_ext X509_get_ext_by_NID X509_get_ext_count X509_get_ext_d2i
X509_get_issuer_name X509_get_serialNumber X509_get_subject_name X509_get_version
X509_get_X509_PUBKEY X509_get0_notBefore X509_get0_notAfter X509_set1_notBefore
X509_set1_notAfter X509_get0_pubkey_bitstr X509_get0_tbs_sigalg X509_issuer_name_hash
X509_NAME_add_entry_by_txt X509_NAME_entry_count X509_NAME_ENTRY_get_data
X509_NAME_ENTRY_get_object X509_NAME_dup X509_NAME_free X509_NAME_get_entry
X509_NAME_get_index_by_NID X509_NAME_get0_der X509_new X509_PUBKEY_get
X509_PUBKEY_get0_param X509_set_ex_data X509_set_issuer_name X509_set_pubkey
X509_set_subject_name X509_sign X509_subject_name_hash X509_STORE_add_cert
X509_STORE_add_crl X509_STORE_CTX_cleanup X509_STORE_CTX_free X509_STORE_CTX_get_current_cert
X509_STORE_CTX_get_error X509_STORE_CTX_get_error_depth X509_STORE_CTX_get0_cert
X509_STORE_CTX_get0_chain X509_STORE_CTX_get0_param X509_STORE_CTX_get0_store
X509_STORE_CTX_get0_untrusted X509_STORE_CTX_get1_chain X509_STORE_CTX_get1_issuer
X509_STORE_CTX_init X509_STORE_CTX_new X509_STORE_CTX_set_flags
X509_STORE_CTX_set_verify_cb X509_STORE_CTX_set_ex_data X509_STORE_CTX_get_ex_data
X509_STORE_CTX_set_error X509_STORE_free X509_STORE_get0_param X509_STORE_new
X509_STORE_set_flags X509V3_EXT_print X509_up_ref X509_verify_cert
X509_verify_cert_error_string X509_VERIFY_PARAM_clear_flags X509_VERIFY_PARAM_get_flags
X509_VERIFY_PARAM_set_time""".split()

# Symbols with real implementations (handled in the hand-written C, not stubbed).
REAL_SYMBOLS = {
    "EVP_MD_CTX_new", "EVP_MD_CTX_free", "EVP_MD_CTX_copy_ex", "EVP_MD_CTX_set_flags",
    "EVP_DigestInit_ex", "EVP_DigestUpdate", "EVP_DigestFinal_ex",
    "EVP_sha1", "EVP_sha256", "EVP_sha384", "EVP_sha512",
    "EVP_sha3_256", "EVP_sha3_384", "EVP_sha3_512",
    "EVP_shake128", "EVP_shake256",
    "EVP_get_digestbyname",
    "EVP_MD_CTX_reset",
    "HMAC", "HMAC_CTX_new", "HMAC_CTX_free", "HMAC_CTX_copy",
    "HMAC_Init_ex", "HMAC_Update", "HMAC_Final",
    "OPENSSL_init_ssl", "OPENSSL_cleanse", "OPENSSL_sk_free", "OPENSSL_sk_new_null",
    "OPENSSL_sk_num", "OPENSSL_sk_pop", "OPENSSL_sk_pop_free", "OPENSSL_sk_push",
    "OPENSSL_sk_value",
    "CRYPTO_malloc", "CRYPTO_zalloc", "CRYPTO_free", "CRYPTO_clear_free",
    "CRYPTO_set_mem_functions", "CRYPTO_get_ex_new_index",
    "ERR_clear_error", "ERR_get_error", "ERR_peek_error", "ERR_peek_last_error",
    "ERR_error_string_n", "ERR_reason_error_string", "ERR_set_mark", "ERR_pop_to_mark",
    "OpenSSL_version_num",
    "BIO_new", "BIO_free", "BIO_read", "BIO_write", "BIO_ctrl", "BIO_ctrl_pending",
    "BIO_new_file", "BIO_s_mem", "BIO_up_ref", "BIO_get_data", "BIO_set_data",
    "BIO_set_init", "BIO_set_flags", "BIO_clear_flags", "BIO_test_flags",
    "BIO_get_new_index", "BIO_meth_new", "BIO_meth_free", "BIO_meth_set_write",
    "BIO_meth_set_read", "BIO_meth_set_create", "BIO_meth_set_destroy",
    "BIO_meth_set_ctrl", "BIO_gets", "BIO_method_type",
    "RAND_bytes", "RAND_poll",
}

# Symbols that go in libssl.so (SSL_* and a few SSL_CTX_*). These are just no-ops;
# XEngine's HarmonyOS player never does TLS.
SSL_SYMBOLS = {s for s in ALL_SYMBOLS if s.startswith("SSL_") or s == "TLS_method"}

# Symbols to stub in libcrypto.so (everything not REAL and not SSL-only).
# Note: SSL_* and TLS_method live in libssl.so; the shim opens libssl.so which
# transitively loads libcrypto.so. We put SSL_* in libssl.so and the rest in libcrypto.so.
CRYPTO_STUB_SYMBOLS = [s for s in ALL_SYMBOLS if s not in REAL_SYMBOLS and s not in SSL_SYMBOLS]
SSL_STUB_SYMBOLS = sorted(SSL_SYMBOLS)

def gen_c():
    stub_funcs = []
    for sym in CRYPTO_STUB_SYMBOLS:
        stub_funcs.append(f"long {sym}(void) {{ return 0; }}")
    stub_block = "\n".join(stub_funcs)
    ssl_funcs = []
    for sym in SSL_STUB_SYMBOLS:
        ssl_funcs.append(f"long {sym}(void) {{ return 0; }}")
    ssl_block = "\n".join(ssl_funcs)
    return stub_block, ssl_block

if __name__ == "__main__":
    stub, ssl = gen_c()
    print(f"crypto stubs: {len(CRYPTO_STUB_SYMBOLS)}, ssl stubs: {len(SSL_STUB_SYMBOLS)}, real: {len(REAL_SYMBOLS)}")
