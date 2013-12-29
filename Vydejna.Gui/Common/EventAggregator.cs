
namespace Vydejna.Gui.Common
{
    public interface IEventPublisher
    {
        void Publish<T>(T msg);
    }
}
