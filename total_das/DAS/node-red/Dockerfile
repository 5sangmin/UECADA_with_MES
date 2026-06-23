# DAS Node-RED image.
# Base: pinned 4.1.10-22 to match line-das, X_DAS, and command-center.
FROM nodered/node-red:4.1.10-22

RUN npm install --no-update-notifier --no-fund \
      node-red-contrib-opcua@0.2.351

COPY patch-opcua-server.js /tmp/patch-opcua-server.js
RUN node /tmp/patch-opcua-server.js
