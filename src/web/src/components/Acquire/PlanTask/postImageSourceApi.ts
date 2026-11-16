import BApi from "@/sdk/BApi";

const requestOptions = { sensitiveResponse: true, showErrorToast: false } as const;

export const postImageSourceApi = {
  list: async (postId: number, signal: AbortSignal) => {
    const result = await BApi.postParser.getPostSourceImages(postId, { ...requestOptions, signal });
    if (result.code || !result.data) throw new Error("imageUnavailable");
    return result.data;
  },
  decode: async (
    postId: number,
    revision: number,
    imageId: string,
    signal: AbortSignal,
    imageData?: string,
  ) => {
    const result = await BApi.postParser.decodePostSourceImage(
      postId,
      { revision, imageId, imageData },
      { ...requestOptions, signal },
    );
    if (result.code || !result.data) throw new Error("imageUnavailable");
    return result.data;
  },
};
