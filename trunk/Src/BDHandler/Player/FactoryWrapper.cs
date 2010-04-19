using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaPortal.Player;
using MediaPortal.GUI.Library;

namespace MediaPortal.Plugins.BDHandler {
    
    public class FactoryWrapper : IPlayerFactory {
        
        IPlayerFactory _defaultFactory;

        public FactoryWrapper(IPlayerFactory factory) {
            this._defaultFactory = factory;
        }

        public IPlayerFactory GetDefaultFactory() {
            return _defaultFactory;
        }

        public IPlayer Create(string filename) {
            return Create(filename, g_Player.MediaType.Video);
        }

        public IPlayer Create(string filename, g_Player.MediaType type) {
            string filepath = filename.ToLower();
            if (filepath.EndsWith(".mpls") || filepath.EndsWith(".bdmv") || (filepath.Contains(@"bdmv\stream") && filepath.EndsWith(".m2ts"))) {
                return new BDPlayer();
            }

            return _defaultFactory.Create(filename, type);
        }

    }
}
