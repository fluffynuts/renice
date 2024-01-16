/// <reference path="../node_modules/zarro/types.d.ts" />
(function () {

  const
    gulp = requireModule<Gulp>("gulp");

  gulp.task("copy-local", async () => {
    const
      { ExecStepContext } = require("exec-step"),
      ctx = new ExecStepContext(),
      os = require("os"),
      path = require("path"),
      exe = "renice.exe",
      { copyFile, CopyFileOptions } = require("yafs"),
      source = path.join("src", "renice", "bin", "Release", "net8.0", "win-x64", "publish", exe),
      target = path.join(os.homedir(), ".local", "bin", exe);

    await ctx.exec(
      `Copy: ${source} -> ${target}`,
      () => copyFile(source, target, CopyFileOptions.overwriteExisting)
    );

  });
})();
