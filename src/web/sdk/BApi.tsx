import toast from 'react-hot-toast';
import type { FullRequestParams, HttpResponse, ApiConfig } from '@/sdk/Api';
import { Api, ContentType } from '@/sdk/Api';
import serverConfig from '@/serverConfig';
import { buildLogger } from '@/components/utils';
import { ErrorToast } from '@/components/Error';

interface BFullRequestParams extends FullRequestParams {
  ignoreError: (rsp) => boolean;
}

interface BResponse {
  code: number;
  message?: string;
}

const log = buildLogger('BApi');

export class BApi extends Api<any> {
  constructor(endpoint: string) {
    log('Initialize new instance with endpoint', endpoint);
    super({
      baseUrl: endpoint,
    });
    const originalRequest = this.request;
    this.request = async <T = any, E = any>(params: BFullRequestParams): Promise<T> => {
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
            if ((typedRsp?.code >= 400 || typedRsp?.code < 200)) {
              if (!params.ignoreError && !typedRsp.message?.includes('SQLite Error')) {
                const title = `[${typedRsp.code}]${params.path}`;
                toast(tst => <ErrorToast toast={tst} title={title} description={typedRsp.message} />, { duration: 5000 });
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

        // console.log(error);

        if (!params.signal?.aborted) {
          const title = `${params.path}: 请求异常，请稍后再试。`;
          let description: string;
          if (typeof error === 'string') {
            description = error;
          } else if (error && typeof error.message === 'string') {
            description = error.message;
          } else if (error && typeof error.toString === 'function') {
            description = error.toString();
          } else {
            description = 'Unknown error';
          }
          toast(tst => <ErrorToast toast={tst} title={title} description={description} />, { duration: 5000 });
        }

        throw error;
      }
    };
  }
}

export default new BApi(serverConfig.apiEndpoint!);
