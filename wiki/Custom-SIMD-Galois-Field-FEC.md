# Custom SIMD Galois Field GF(2^8) Reed-Solomon Forward Error Correction

Moonshine implements a custom-engineered, multi-tiered SIMD Galois Field $GF(2^8)$ Reed-Solomon Forward Error Correction (FEC) engine supporting Intel GFNI (Galois Field New Instructions), AVX-512BW, AVX2, and 64-bit scalar execution paths.

---

## 1. Mathematical Formulation

### A. Generator Polynomial
The Galois Field $GF(2^8)$ arithmetic in Moonshine uses the primitive generator polynomial:
$$P(x) = x^8 + x^4 + x^3 + x^2 + 1 \quad (\text{0x11D} \text{ / } \text{0x1D})$$

Multiplication of two elements $\alpha, \beta \in GF(2^8)$ corresponds to polynomial multiplication modulo $P(x)$ over $GF(2)$:
$$\alpha \cdot \beta = (\alpha(x) \times \beta(x)) \pmod{P(x)}$$

Logarithmic and exponential exponent tables are precomputed for $O(1)$ scalar multiplication:
$$\alpha \cdot \beta = \exp\Big((\log(\alpha) + \log(\beta)) \pmod{255}\Big)$$

---

## 2. Multi-Tiered SIMD Execution Hierarchy

```
┌────────────────────────────────────────────────────────┐
│               ReedSolomonSimd Dispatcher               │
└───────────────────────────┬────────────────────────────┘
                            │
       ┌────────────────────┼────────────────────┐
       ▼                    ▼                    ▼
┌───────────────┐    ┌───────────────┐    ┌───────────────┐
│ Intel GFNI +  │    │  AVX-512BW /  │    │  Scalar /     │
│ AVX-512 (ZMM) │    │  AVX2 (YMM)   │    │  64-bit Tail  │
│ 64 Bytes/inst │    │ Nibble Table  │    │ Word Fallback │
└───────────────┘    └───────────────┘    └───────────────┘
```

### Tier 1: Intel GFNI + AVX-512 (64 Bytes Per Clock)
On Intel Ice Lake, Alder Lake / Raptor Lake, Sapphire Rapids, and AMD Zen 4 / Zen 5 CPUs with `GFNI` and `AVX512F`:
- Utilizes `_mm512_gf2p8mul_epi8` for tableless single-cycle parallel multiplication of 64 bytes in 512-bit ZMM registers.
- Accumulates parity shards with `_mm512_xor_si512`.

```cpp
__m512i v_coeff = _mm512_set1_epi8(coeff);
__m512i v_src   = _mm512_loadu_si512(src + i);
__m512i v_dest  = _mm512_loadu_si512(dest + i);
__m512i v_prod  = _mm512_gf2p8mul_epi8(v_src, v_coeff);
_mm512_storeu_si512(dest + i, _mm512_xor_si512(v_dest, v_prod));
```

### Tier 2: AVX2 4-Bit Nibble Decomposition (32 Bytes Per Clock)
On AVX2 hardware without GFNI, each byte $b$ is decomposed into low nibble $b_L = b \ \& \ \text{0x0F}$ and high nibble $b_H = (b \gg 4) \ \& \ \text{0x0F}$:
$$b \cdot c = (b_L \cdot c) \oplus (b_H \cdot c)$$

- Lookups are executed simultaneously across 32 bytes using `_mm256_shuffle_epi8` (`vpshufb`).
- Results are recombined using `_mm256_xor_si256`.

---

## 3. Dynamic CPU Feature Detection

At runtime, the engine queries CPUID leaf 7:
- **AVX2**: `EBX` bit 5
- **AVX-512F**: `EBX` bit 16
- **AVX-512BW**: `EBX` bit 30
- **GFNI**: `ECX` bit 8

The active instruction set can be queried via `moonshine_fec_get_simd_architecture()`.
