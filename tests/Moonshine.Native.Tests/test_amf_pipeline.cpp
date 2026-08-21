#include "moonshine/export/moonshine_native_api.h"
#include <cassert>
int main() { for (uint32_t codec = 0; codec <= 3; ++codec) { uint32_t supported = 1; if (moonshine_amf_query_codec_support(codec, &supported) != 1 || supported != 0) return 1; } return 0; }
