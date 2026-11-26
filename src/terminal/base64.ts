// Base64 to bytes, for the host's output messages, a program's OSC 52 copy and the kitty images. A plain loop:
// Uint8Array.from with a callback calls the function once per byte, several times slower on a burst of output.
export const decodeBase64 = (data: string): Uint8Array<ArrayBuffer> => {
  const binary = atob(data);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  return bytes;
};
