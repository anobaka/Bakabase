import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { Card, CardBody } from '@/components/bakaui';

type Props = {
  id: any;
  name: any;
  idx: number;
};

export default ({ id, name, idx }: Props) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
  } = useSortable({ id: id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div ref={setNodeRef} style={style} {...attributes} {...listeners}>
      <Card>
        <CardBody>
          [{idx + 1}]
          {name}
        </CardBody>
      </Card>
    </div>
  );
};
