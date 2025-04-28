using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Models.Entities
{
	public record CategoryCustomPropertyMapping : IEquatable<CategoryCustomPropertyMapping>
	{
		public int Id { get; set; }
		public int CategoryId { get; set; }
		public int PropertyId { get; set; }
		public int Order { get; set; }

		public virtual bool Equals(CategoryCustomPropertyMapping? other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return Id == other.Id && CategoryId == other.CategoryId && PropertyId == other.PropertyId;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(CategoryId, PropertyId);
		}
	}
}