import type { FullRequestParams } from "@/sdk/Api";

import toast from "react-hot-toast";

import { Api } from "@/sdk/Api";
import envConfig from "@/config/env";
import { buildLogger } from "@/components/utils";

interface BFullRequestParams extends FullRequestParams {
  ignoreError: (rsp) => boolean;
}

interface BResponse {
  code: number;
  message?: string;
}

const log = buildLogger("BApi");

export class BApi extends Api<any> {
  constructor(endpoint: string) {
    log("Initialize new instance with endpoint", endpoint);
    super({
      baseUrl: endpoint,
    });
    const originalRequest = this.request;

    this.request = async <T = any, E = any>(
      params: BFullRequestParams,
    ): Promise<T> => {
      try {
        log(params);
        const rsp = await originalRequest<T, E>(params);
        const typedRsp = rsp as BResponse;

        switch (typedRsp?.code) {
          case 0:
            break;
          default:
            if (params.ignoreError) {
              if (params.ignoreError(rsp)) {
                return rsp;
              }
            }
            if (typedRsp?.code >= 400 || typedRsp?.code < 200) {
              if (
                !params.ignoreError &&
                !typedRsp.message?.includes("SQLite Error")
              ) {
                const title = `[${typedRsp.code}]${params.path}`;

                const { ErrorToast } = require("@/components/Error");

                toast(
                  (tst) => (
                    <ErrorToast
                      description={typedRsp.message}
                      title={title}
                      toast={tst}
                    />
                  ),
                  { duration: 5000 },
                );
              }
            }
        }

        return rsp;
      } catch (error) {
        // switch (error.code) {
        //   case 'ERR_CANCELED': {
        //     return;
        //   }
        // }

        log(error);

        if (!params.signal?.aborted) {
          const title = `${params.path}: 请求异常，请稍后再试。`;
          let description: string;

          if (typeof error === "string") {
            description = error;
          } else if (error && typeof error.message === "string") {
            description = error.message;
          } else if (error && typeof error.toString === "function") {
            description = error.toString();
          } else {
            description = "Unknown error";
          }
          const { ErrorToast } = require("@/components/Error");

          toast(
            (tst) => (
              <ErrorToast description={description} title={title} toast={tst} />
            ),
            { duration: 5000 },
          );
        }

        throw error;
      }
    };
  }
}

export default new BApi(envConfig.apiEndpoint);
