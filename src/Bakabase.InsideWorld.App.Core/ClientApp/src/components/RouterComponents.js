import qs from 'qs';
import {
  appConfig
} from '../appConfig';

export const buildLoginRedirectPath = function (redirectPath) {
  redirectPath = redirectPath || location.hash;
  // console.log(redirectPath)
  return `${appConfig.loginPath}?redirect=${encodeURI(redirectPath)}`;
};

export const redirect = function (hashOrUri) {
  if (!hashOrUri.startsWith('#')) {
    if (!hashOrUri.startsWith('/')) {
      hashOrUri = `/${hashOrUri}`;
    }
    // if (hashOrUri.indexOf('entry') < 0) {
    //   hashOrUri = `#${buildSysSpecificUrl(hashOrUri)}`;
    // }
  }
  location.hash = hashOrUri;
};

export const getQueryParams = function (props) {
  return qs.parse(props.location.search.substring(1));
};

export const getSysKey = function () {
  const { hash } = location;
  const firstSlash = hash.indexOf('/');
  const secondSlash = hash.indexOf('/', firstSlash + 1);
  if (firstSlash > -1 && secondSlash > -1) {
    const sysKey = hash.substring(firstSlash + 1, secondSlash);
    if (/^(loms|lms|moms|oms|fms)$/.test(sysKey)) {
      return sysKey;
    }
  }
  return '';
};

export const buildSysSpecificUrl = function (url) {
  const sysKey = getSysKey();
  if (sysKey) {
    url = url.replace(sysKey, '');
  }
  return `/${sysKey}/${url}`.replace(/\/+/g, '/');
};

export const inLoms = () => getSysKey() == 'loms';
export const inLms = () => getSysKey() == 'lms';
export const inMoms = () => getSysKey() == 'moms';
export const inOms = () => getSysKey() == 'oms';
export const inFms = () => getSysKey() == 'fms';
