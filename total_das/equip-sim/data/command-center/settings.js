// Command Center Node-RED settings.
// line-das 의 settings.js 와 동일한 최소 설정 + flowFile 환경변수 지원.
module.exports = {
  flowFile: process.env.FLOWS || "flows.json",
  flowFilePretty: true
};
