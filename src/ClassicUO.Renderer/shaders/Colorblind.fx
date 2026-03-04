float4x4 MatrixTransform;
float3 Row0;  // first row of the 3x3 color matrix
float3 Row1;  // second row
float3 Row2;  // third row

sampler decal : register(s0);

struct VS_INPUT
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float3 TexCoord : TEXCOORD0;
    float3 Hue      : TEXCOORD1;
};

struct PS_INPUT
{
    float4 Position : POSITION0;
    float3 TexCoord : TEXCOORD0;
};

PS_INPUT main_vertex(VS_INPUT IN)
{
    PS_INPUT OUT;
    OUT.Position = mul(IN.Position, MatrixTransform);
    OUT.TexCoord = IN.TexCoord;
    return OUT;
}

float4 main_fragment(PS_INPUT IN) : COLOR0
{
    float4 color = tex2D(decal, IN.TexCoord.xy);

    float3 rgb = color.rgb;
    float3 transformed;
    transformed.r = dot(rgb, Row0);
    transformed.g = dot(rgb, Row1);
    transformed.b = dot(rgb, Row2);

    return float4(saturate(transformed), color.a);
}

technique T0
{
    pass P0
    {
        VertexShader = compile vs_2_0 main_vertex();
        PixelShader = compile ps_2_0 main_fragment();
    }
}
