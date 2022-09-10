# URPSeparableSSS
Separable Subsurface Scatter for URP 10.x or later

As a postprocessing effect, using stencil buffer to improve GPU performance

<p align="center">
  <img src="https://github.com/bearworks/URPSeparableSSS/blob/main/Image.png">
</p>

UnityChanSSU assets are used in this project

Reference:
https://github.com/iryoku/separable-sss


## Usage

1.Insert Stencil { Ref[_StencilNo] Comp always  Pass Replace } to your objects' shader code.  
2.The _StencilNo property of the objects' shader material needs to match the SSSS postprocess Ref Value.
