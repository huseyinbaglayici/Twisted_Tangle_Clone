using Reflex.Core;
using Reflex.Injectors;
using Runtime.Audio;
using Runtime.Boostrap;
using Runtime.Boostrap.Save;
using Runtime.Effects;
using UnityEngine;

namespace Runtime._Installers
{
    public class RootInstaller : MonoBehaviour, IInstaller
    {
        [SerializeField] private FxPlayer fxPlayerPrefab;
        [SerializeField] private AudioPlayer audioPlayerPrefab;

        public void InstallBindings(ContainerBuilder builder)
        {
            builder.RegisterValue(new SaveService());
            builder.RegisterValue(new SceneFlowService());

            // Effects and sounds outlive the menu / gameplay swaps, so they are created once with the root container.
            var fx = Spawn(fxPlayerPrefab);
            var audio = Spawn(audioPlayerPrefab);
            builder.RegisterValue(fx);
            builder.RegisterValue(audio);
            builder.OnContainerBuilt += container => GameObjectInjector.InjectObject(audio.gameObject, container);
        }

        private static T Spawn<T>(T prefab) where T : Component
        {
            var instance = Instantiate(prefab);
            instance.name = prefab.name;
            DontDestroyOnLoad(instance.gameObject);
            return instance;
        }
    }
}
