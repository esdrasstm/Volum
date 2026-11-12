#pragma once

// Identidade COM do equalizador. Estes GUIDs são do Volum; não vêm de outro projeto.

#include <guiddef.h>

// {7E4C1A2B-9F30-4D55-A1C8-6B2E0F5D3A17}
static const GUID CLSID_VolumEqApo = {
    0x7e4c1a2b, 0x9f30, 0x4d55, { 0xa1, 0xc8, 0x6b, 0x2e, 0x0f, 0x5d, 0x3a, 0x17 }
};

// Propriedades nossas no FxProperties do dispositivo.
// pid 1: id do endpoint (o mesmo IMMDevice::GetId). pid 2: CLSID do efeito que já estava, para encadear.
// {C2A91F40-5E77-4B18-9D3C-81F6A0B24E55}
static const GUID VOLUM_EQ_PROP = {
    0xc2a91f40, 0x5e77, 0x4b18, { 0x9d, 0x3c, 0x81, 0xf6, 0xa0, 0xb2, 0x4e, 0x55 }
};

#define VOLUM_EQ_PROP_ENDPOINT 1
#define VOLUM_EQ_PROP_CHILD 2
