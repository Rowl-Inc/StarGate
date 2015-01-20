using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.App.StarGate.Repo
{
	[Flags]
	public enum FinderMatchPart : int
	{
		None = 0,
		ShardId = 1,
		Host = 2,
		All = ShardId | Host,
	}
}
