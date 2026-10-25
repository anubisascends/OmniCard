// Stand-in for @pdfme/converter (aliased in vite.config.ts). The pdfme designer only needs it to
// rasterize an uploaded base PDF, and labels always use a blank page — so the real converter (a
// ~20 MB PDF engine: wasm + worker) is left out of the build.

const unsupported = (): never => {
  throw new Error('Background PDFs are not supported on labels.');
};

export const pdf2img = async (): Promise<ArrayBuffer[]> => unsupported();
export const pdf2size = async (): Promise<{ width: number; height: number }[]> => unsupported();
export const img2pdf = async (): Promise<ArrayBuffer> => unsupported();
