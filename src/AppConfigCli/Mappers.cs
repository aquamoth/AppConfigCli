using Riok.Mapperly.Abstractions;
using AppConfigCli.Core;

namespace AppConfigCli;

[Mapper]
internal partial class EditorMappers
{
    public partial Item ToUiItem(Core.Item item);
    [MapperIgnoreSource(nameof(Item.IsNew))]
    [MapperIgnoreSource(nameof(Item.IsDeleted))]
    public partial Core.Item ToCoreItem(Item item);
}
