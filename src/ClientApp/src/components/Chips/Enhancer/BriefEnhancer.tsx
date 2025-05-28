import { EnhancerIcon } from '@/components/Enhancer';

type Props = {
  enhancer: {id: number; name: string};
};

export default ({ enhancer }: Props) => {
  return (
    <div className={'flex items-center gap-1'}>
      <EnhancerIcon id={enhancer.id} />
      {enhancer.name}
    </div>
  );
};
