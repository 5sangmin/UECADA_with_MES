# X_DAS Node-RED image.
# Base: pinned 4.1.10-22 to match line-das and command-center.
FROM nodered/node-red:4.1.10-22

# Required contrib nodes (versions pinned for reproducibility):
#  - node-red-contrib-opcua      : OPC UA client/server bridge
#  - node-red-contrib-postgresql : TSDB / CommandDB access
RUN npm install --unsafe-perm --no-update-notifier --no-fund \
      node-red-contrib-opcua@0.2.351 \
      node-red-contrib-postgresql@0.15.4

COPY docker/node-red/patch-opcua-server.js /tmp/patch-opcua-server.js
RUN node /tmp/patch-opcua-server.js

RUN node -e "const path = require('path'); const opcua = require('node-opcua'); const manager = new opcua.OPCUACertificateManager({ rootFolder: path.join(process.env.HOME || '/usr/src/node-red', '.config/node-red-opcua-nodejs/PKI'), automaticallyAcceptUnknownCertificate: true }); manager.initialize().then(() => console.log('OPC UA client PKI initialized')).catch((error) => { console.error(error); process.exit(1); });"
