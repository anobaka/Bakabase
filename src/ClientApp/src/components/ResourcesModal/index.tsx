import { useState } from 'react';
import type { Resource } from '@/core/models/Resource';
import type { Pageable } from '@/components/types';
import BApi from '@/sdk/BApi';
import type { components } from '@/sdk/BApi2';

type Form = components['schemas']['Bakabase.Service.Models.Input.ResourceSearchInputModel'];

type Props = {
  form?: Form;
};

export default ({ form }: Props) => {
  const [resources, setResources] = useState<Resource[]>([]);
  const [pageable, setPageable] = useState<Pageable>({
    page: 1,
    pageSize: 20,
  });

  const search = async () => {
    const rsp = await BApi.resource.searchResources({
      ...form,
      page: pageable.page,
      pageSize: pageable.pageSize,
    });

    setPageable({
      page: rsp.pageIndex,
      pageSize: rsp.pageSize,
      total: rsp.totalCount,
      totalPage: Math.ceil(rsp.totalCount / rsp.pageSize),
    });
    setResources(resources);
  };

  return (
    <div>
      123
    </div>
  );
};
