import axios, { AxiosRequestConfig } from 'axios';
import qs from 'qs';
import {
  Message,
} from '@alifd/next';
import i18n from 'i18next';
import serverConfig from '../serverConfig';

export default function request(
  method,
  url,
  body,
  queryParameters,
  form,
  config,
) {
  if (serverConfig.beforeRequesting) {
    serverConfig.beforeRequesting(method, url, body, queryParameters, form, config);
  }
  if (url.indexOf('://') < 0) {
    url = serverConfig.apiEndpoint + url;
  }
  method = method.toLowerCase();
  const returnObj = {};
  const options = {
    url,
    method,
    params: {
      ...queryParameters,
      _t: new Date().getTime(),
    },
    ...config,
  };
  if (body) {
    options.data = body;
  } else if (method !== 'get') {
    options.data = qs.stringify(form);
  }
  // 默认的会导致array形式的query param在.net侧无法解析
  options.paramsSerializer = function (params) {
    return qs.stringify(params);
  };
  options.maxRedirects = 0;
  options.withCredentials = true;
  returnObj.options = options;
  returnObj.invoke = function (callback = undefined) {
    const promise = new Promise((resolve, reject) => {
      axios
        .request(options)
        .then((res) => {
          if (res.status === 200) {
            switch (res.data.code) {
              case 0:
                resolve(res.data);
                break;
              default:
                if ((res.data.code >= 400 || res.data.code < 200) && (!config?.ignoreError || !config.ignoreError(res.data))) {
                  Message.error({
                    duration: 0,
                    title: `${url}: [${res.data.code}]`,
                    content: (
                      <pre>
                        {i18n.t(res.data.message)}
                      </pre>
                    ),
                    closeable: true,
                  });
                }
                resolve(res.data);
                break;
            }
          } else {
            reject(res);
            Message.error({
              duration: 0,
              title: `${url}: 请求异常，请稍后再试`,
              closeable: true,
            });
          }
        })
        .catch((error) => {
          switch (error.code) {
            case 'ERR_CANCELED':
            {
              return;
            }
          }
          reject(error);
          Message.error({
            duration: 0,
            title: `${url}: 请求异常，请稍后再试。`,
            content: (
              <pre>
                {error}
              </pre>
            ),
            closeable: true,
          });
        });
    });
    if (callback) {
      return promise.then(callback);
    }
    return promise;
  };
  return returnObj;
}
